using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
            if (!string.IsNullOrEmpty(ErrorMessage))
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

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user =
                await _userManager
                    .FindByEmailAsync(Input.Email);

            if (user == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            var isEmployee =
                await _userManager
                    .IsInRoleAsync(
                        user,
                        "Employee");

            var isAdmin =
                await _userManager
                    .IsInRoleAsync(
                        user,
                        "Admin");

            var isSuperAdmin =
                await _userManager
                    .IsInRoleAsync(
                        user,
                        "SuperAdmin");

            var isEmployeeOnly =
                isEmployee &&
                !isAdmin &&
                !isSuperAdmin;

            /*
             * ========================================================
             * EMPLOYEE SINGLE-DEVICE CHECK
             * ========================================================
             *
             * Admin / SuperAdmin are NOT restricted.
             *
             * Employee:
             *
             * Device A -> login -> allowed
             * Device B -> login -> blocked
             *
             * Device A -> real logout
             * Device B -> login -> allowed
             */

            if (isEmployeeOnly)
            {
                var deviceId =
                    GetOrCreateDeviceId();

                var deviceAvailable =
                    await TryAcquireEmployeeDeviceAsync(
                        user,
                        deviceId);

                if (!deviceAvailable)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }
            }

            var result =
                await _signInManager
                    .PasswordSignInAsync(
                        user,
                        Input.Password,
                        Input.RememberMe,
                        lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "User logged in: {Email}",
                    user.Email);

                if (isEmployeeOnly)
                {
                    return LocalRedirect(
                        "~/employee-home");
                }

                if (
                    Url.IsLocalUrl(returnUrl) &&
                    returnUrl != "/")
                {
                    return LocalRedirect(
                        returnUrl);
                }

                return LocalRedirect("~/");
            }

            /*
             * Login failed.
             *
             * If employee device was reserved but password failed,
             * release it so the user is not locked out accidentally.
             */
            if (isEmployeeOnly)
            {
                var deviceId =
                    GetExistingDeviceId();

                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    await ReleaseEmployeeDeviceAsync(
                        user,
                        deviceId);
                }
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning(
                    "User account locked out: {Email}",
                    user.Email);

                return RedirectToPage(
                    "./Lockout");
            }

            ModelState.AddModelError(
                string.Empty,
                "Invalid login attempt.");

            return Page();
        }

        // ============================================================
        // DEVICE ID
        // ============================================================

        private string GetOrCreateDeviceId()
        {
            if (
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var existing) &&
                !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var deviceId =
                Guid.NewGuid().ToString("N");

            Response.Cookies.Append(
                DeviceCookieName,
                deviceId,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365)
                });

            return deviceId;
        }

        private string? GetExistingDeviceId()
        {
            if (
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var existing) &&
                !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            return null;
        }

        // ============================================================
        // ACQUIRE DEVICE
        // ============================================================

        private async Task<bool> TryAcquireEmployeeDeviceAsync(
            IdentityUser user,
            string deviceId)
        {
            await using var db =
                await _dbFactory
                    .CreateDbContextAsync();

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var token =
                    await db.Set<IdentityUserToken<string>>()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == user.Id &&
                                x.LoginProvider == LoginProvider &&
                                x.Name == TokenName);

                if (
                    token != null &&
                    !string.IsNullOrWhiteSpace(token.Value))
                {
                    /*
                     * Same browser/device:
                     * allow continued login.
                     */
                    if (token.Value == deviceId)
                    {
                        await transaction.CommitAsync();
                        return true;
                    }

                    /*
                     * Another browser/device is already active.
                     */
                    await transaction.RollbackAsync();

                    _logger.LogWarning(
                        "Employee login blocked because another device is active. UserId={UserId}",
                        user.Id);

                    return false;
                }

                db.Set<IdentityUserToken<string>>()
                    .Add(
                        new IdentityUserToken<string>
                        {
                            UserId = user.Id,
                            LoginProvider = LoginProvider,
                            Name = TokenName,
                            Value = deviceId
                        });

                await db.SaveChangesAsync();

                await transaction.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                _logger.LogError(
                    ex,
                    "Unable to acquire employee device lock for UserId={UserId}",
                    user.Id);

                throw;
            }
        }

        // ============================================================
        // RELEASE DEVICE
        // ============================================================

        private async Task ReleaseEmployeeDeviceAsync(
            IdentityUser user,
            string deviceId)
        {
            await using var db =
                await _dbFactory
                    .CreateDbContextAsync();

            var token =
                await db.Set<IdentityUserToken<string>>()
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.LoginProvider == LoginProvider &&
                            x.Name == TokenName);

            if (
                token != null &&
                token.Value == deviceId)
            {
                db.Remove(token);

                await db.SaveChangesAsync();
            }
        }
    }
}