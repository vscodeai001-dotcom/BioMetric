using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payroll.Shared.Data;

namespace Payroll.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private const string LoginProvider =
            "BioMetric";

        private const string TokenName =
            "ActiveDevice";

        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _dbFactory = dbFactory;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = string.Empty;

        [TempData]
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public void OnGet(string? returnUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                ModelState.AddModelError(
                    string.Empty,
                    ErrorMessage);
            }

            ReturnUrl =
                returnUrl ??
                Url.Content("~/") ??
                "/";
        }

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            returnUrl ??=
                Url.Content("~/") ??
                "/";

            ReturnUrl = returnUrl;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user =
                await _userManager.FindByEmailAsync(
                    Input.Email);

            if (user == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            // ========================================================
            // CHECK ROLE
            // ========================================================

            var isEmployee =
                await _userManager.IsInRoleAsync(
                    user,
                    "Employee");

            var isAdmin =
                await _userManager.IsInRoleAsync(
                    user,
                    "Admin");

            var isSuperAdmin =
                await _userManager.IsInRoleAsync(
                    user,
                    "SuperAdmin");

            var isEmployeeOnly =
                isEmployee &&
                !isAdmin &&
                !isSuperAdmin;

            // ========================================================
            // VERIFY PASSWORD FIRST
            // ========================================================
            //
            // IMPORTANT:
            // Do NOT reserve the device before checking the password.
            //
            // This prevents a failed login attempt from temporarily
            // locking the employee account.
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);

            if (passwordResult.IsLockedOut)
            {
                _logger.LogWarning(
                    "User account locked out: {Email}",
                    user.Email);

                return RedirectToPage(
                    "./Lockout");
            }

            if (!passwordResult.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            // ========================================================
            // EMPLOYEE SINGLE-DEVICE LOCK
            // ========================================================

            if (isEmployeeOnly)
            {
                var deviceId =
                    GetExistingDeviceId();

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId =
                        Guid.NewGuid().ToString("N");
                }

                var acquired =
                    await TryAcquireEmployeeDeviceAsync(
                        user,
                        deviceId);

                if (!acquired)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }

                // Only create the device cookie AFTER the database
                // lock has been successfully acquired.
                SetDeviceCookie(deviceId);
            }

            // ========================================================
            // CREATE IDENTITY LOGIN SESSION
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "User logged in successfully: {Email}",
                user.Email);

            // ========================================================
            // EMPLOYEE
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect(
                    "~/employee-home");
            }

            // ========================================================
            // ADMIN / SUPER ADMIN
            // ========================================================

            if (
                Url.IsLocalUrl(returnUrl) &&
                returnUrl != "/")
            {
                return LocalRedirect(returnUrl);
            }

            return LocalRedirect("~/");
        }

        // ============================================================
        // DEVICE COOKIE
        // ============================================================

        private string? GetExistingDeviceId()
        {
            if (
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) &&
                !string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId;
            }

            return null;
        }

        private void SetDeviceCookie(
            string deviceId)
        {
            Response.Cookies.Append(
                DeviceCookieName,
                deviceId,
                new CookieOptions
                {
                    HttpOnly = true,

                    Secure = Request.IsHttps,

                    SameSite = SameSiteMode.Lax,

                    IsEssential = true,

                    MaxAge =
                        TimeSpan.FromDays(365)
                });
        }

        // ============================================================
        // ACQUIRE EMPLOYEE DEVICE
        // ============================================================

        private async Task<bool>
            TryAcquireEmployeeDeviceAsync(
                IdentityUser user,
                string deviceId)
        {
            await using var db =
                await _dbFactory
                    .CreateDbContextAsync();

            /*
             * SERIALIZABLE prevents two devices from both seeing
             * "no active device" and acquiring the account together.
             */
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var token =
                    await db
                        .Set<IdentityUserToken<string>>()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == user.Id &&
                                x.LoginProvider ==
                                    LoginProvider &&
                                x.Name ==
                                    TokenName);

                // ====================================================
                // AN ACTIVE DEVICE ALREADY EXISTS
                // ====================================================

                if (token != null &&
                    !string.IsNullOrWhiteSpace(token.Value))
                {
                    // Same device/browser.
                    if (token.Value == deviceId)
                    {
                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "Employee continued from existing device. UserId={UserId}",
                            user.Id);

                        return true;
                    }

                    // Different device.
                    await transaction.RollbackAsync();

                    _logger.LogWarning(
                        "Employee login blocked. Another device is already active. UserId={UserId}",
                        user.Id);

                    return false;
                }

                // ====================================================
                // NO ACTIVE DEVICE
                // ====================================================

                db.Set<IdentityUserToken<string>>()
                    .Add(
                        new IdentityUserToken<string>
                        {
                            UserId = user.Id,
                            LoginProvider =
                                LoginProvider,
                            Name =
                                TokenName,
                            Value =
                                deviceId
                        });

                await db.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Employee device lock acquired. UserId={UserId}",
                    user.Id);

                return true;
            }
            catch (DbUpdateException ex)
                when (
                    ex.InnerException
                        is PostgresException pg &&
                    pg.SqlState ==
                        PostgresErrorCodes.UniqueViolation)
            {
                /*
                 * Two devices attempted login at exactly the same
                 * time. PostgreSQL allowed only one token.
                 *
                 * Therefore this device loses the race and must
                 * be rejected.
                 */

                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Ignore rollback failure.
                }

                _logger.LogWarning(
                    "Concurrent employee login rejected. UserId={UserId}",
                    user.Id);

                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Ignore rollback failure.
                }

                _logger.LogError(
                    ex,
                    "Error acquiring employee device lock. UserId={UserId}",
                    user.Id);

                throw;
            }
        }
    }
}