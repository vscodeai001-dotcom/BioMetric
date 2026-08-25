using System.ComponentModel.DataAnnotations;
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

        // ============================================================
        // INPUT
        // ============================================================

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = "/";

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

        // ============================================================
        // GET
        // ============================================================

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

        // ============================================================
        // POST LOGIN
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            ReturnUrl =
                returnUrl ??
                Url.Content("~/") ??
                "/";

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // ========================================================
            // FIND USER
            // ========================================================

            var user =
                await _userManager.FindByEmailAsync(
                    Input.Email);

            if (user == null)
            {
                _logger.LogWarning(
                    "LOGIN FAILED: User not found. Email={Email}",
                    Input.Email);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            // ========================================================
            // CHECK ROLES
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

            _logger.LogInformation(
                "LOGIN: User={Email}, Employee={Employee}, Admin={Admin}, SuperAdmin={SuperAdmin}",
                user.Email,
                isEmployee,
                isAdmin,
                isSuperAdmin);

            // ========================================================
            // VERIFY PASSWORD
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);

            if (passwordResult.IsLockedOut)
            {
                _logger.LogWarning(
                    "LOGIN: Account locked. UserId={UserId}",
                    user.Id);

                return RedirectToPage("./Lockout");
            }

            if (!passwordResult.Succeeded)
            {
                _logger.LogWarning(
                    "LOGIN FAILED: Invalid password. UserId={UserId}",
                    user.Id);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            _logger.LogInformation(
                "LOGIN: Password verified. UserId={UserId}",
                user.Id);

            // ========================================================
            // EMPLOYEE SINGLE DEVICE LOCK
            // ========================================================

            if (isEmployeeOnly)
            {
                var deviceId =
                    GetOrCreateDeviceId();

                _logger.LogInformation(
                    "DEVICE LOGIN ATTEMPT: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                var allowed =
                    await AcquireEmployeeDeviceAsync(
                        user,
                        deviceId);

                if (!allowed)
                {
                    _logger.LogWarning(
                        "DEVICE LOGIN BLOCKED: Another device is already active. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }

                // Device lock was successfully acquired.
                SetDeviceCookie(deviceId);

                _logger.LogInformation(
                    "DEVICE LOCK ACTIVE: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // SIGN IN
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "LOGIN SUCCESS: UserId={UserId}, Email={Email}",
                user.Id,
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
            // ADMIN / SUPERADMIN
            // ========================================================

            if (
                Url.IsLocalUrl(ReturnUrl) &&
                ReturnUrl != "/")
            {
                return LocalRedirect(ReturnUrl);
            }

            return LocalRedirect("~/");
        }

        // ============================================================
        // DEVICE ID
        // ============================================================

        private string GetOrCreateDeviceId()
        {
            if (
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var existingDeviceId) &&
                !string.IsNullOrWhiteSpace(existingDeviceId))
            {
                return existingDeviceId;
            }

            return Guid.NewGuid().ToString("N");
        }

        // ============================================================
        // SAVE DEVICE COOKIE
        // ============================================================

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
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/"
                });
        }

        // ============================================================
        // ACQUIRE SINGLE EMPLOYEE DEVICE
        // ============================================================

        private async Task<bool> AcquireEmployeeDeviceAsync(
            IdentityUser user,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            await using var transaction =
                await db.Database.BeginTransactionAsync();

            try
            {
                // ----------------------------------------------------
                // PostgreSQL transaction advisory lock
                // ----------------------------------------------------

                var lockKey =
                    CreateStableLockKey(user.Id);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    SELECT pg_advisory_xact_lock({lockKey})
                    """);

                _logger.LogInformation(
                    "DEVICE LOCK: PostgreSQL lock acquired. UserId={UserId}",
                    user.Id);

                // ----------------------------------------------------
                // Read existing ActiveDevice
                // ----------------------------------------------------

                var existingToken =
                    await db
                        .Set<IdentityUserToken<string>>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == user.Id &&
                                x.LoginProvider == LoginProvider &&
                                x.Name == TokenName);

                // ----------------------------------------------------
                // EXISTING DEVICE
                // ----------------------------------------------------

                if (
                    existingToken != null &&
                    !string.IsNullOrWhiteSpace(
                        existingToken.Value))
                {
                    _logger.LogInformation(
                        "DEVICE LOCK: Existing ActiveDevice found. UserId={UserId}",
                        user.Id);

                    // Same browser/device is allowed to continue.
                    if (
                        string.Equals(
                            existingToken.Value,
                            deviceId,
                            StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "DEVICE LOCK: Same device confirmed. UserId={UserId}",
                            user.Id);

                        await transaction.CommitAsync();

                        return true;
                    }

                    // Different device is blocked.
                    _logger.LogWarning(
                        "DEVICE LOCK: SECOND DEVICE BLOCKED. UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        user.Id,
                        existingToken.Value,
                        deviceId);

                    await transaction.RollbackAsync();

                    return false;
                }

                // ----------------------------------------------------
                // NO DEVICE EXISTS
                // ----------------------------------------------------

                _logger.LogInformation(
                    "DEVICE LOCK: No ActiveDevice exists. Creating one. UserId={UserId}",
                    user.Id);

                // ----------------------------------------------------
                // Use ASP.NET Core Identity token API
                // ----------------------------------------------------

                var tokenResult =
                    await _userManager.SetAuthenticationTokenAsync(
                        user,
                        LoginProvider,
                        TokenName,
                        deviceId);

                if (tokenResult != null)
                {
                    // This should normally be IdentityResult.Success,
                    // but log failures defensively.
                    if (!tokenResult.Succeeded)
                    {
                        _logger.LogError(
                            "DEVICE LOCK: Identity token creation failed. UserId={UserId}, Errors={Errors}",
                            user.Id,
                            string.Join(
                                "; ",
                                tokenResult.Errors.Select(
                                    e =>
                                        $"{e.Code}:{e.Description}")));

                        await transaction.RollbackAsync();

                        return false;
                    }
                }

                // ----------------------------------------------------
                // Verify immediately using Identity API
                // ----------------------------------------------------

                var savedDeviceId =
                    await _userManager.GetAuthenticationTokenAsync(
                        user,
                        LoginProvider,
                        TokenName);

                if (
                    string.IsNullOrWhiteSpace(
                        savedDeviceId) ||
                    !string.Equals(
                        savedDeviceId,
                        deviceId,
                        StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "DEVICE LOCK: ActiveDevice verification FAILED. UserId={UserId}",
                        user.Id);

                    await transaction.RollbackAsync();

                    return false;
                }

                _logger.LogInformation(
                    "DEVICE LOCK: ActiveDevice VERIFIED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                await transaction.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                _logger.LogError(
                    ex,
                    "DEVICE LOCK: Exception while acquiring device lock. UserId={UserId}",
                    user.Id);

                return false;
            }
        }

        // ============================================================
        // STABLE POSTGRES LOCK KEY
        // ============================================================

        private static long CreateStableLockKey(
            string userId)
        {
            unchecked
            {
                long hash = 17;

                foreach (var c in userId)
                {
                    hash =
                        hash * 31 +
                        c;
                }

                return hash;
            }
        }
    }
}