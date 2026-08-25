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
        // POST
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            returnUrl =
                returnUrl ??
                Url.Content("~/") ??
                "/";

            ReturnUrl = returnUrl;

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
                    "BIOMETRIC LOGIN: User not found. Email={Email}",
                    Input.Email);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            // ========================================================
            // ROLES
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
                "BIOMETRIC LOGIN: User={Email}, Employee={Employee}, Admin={Admin}, SuperAdmin={SuperAdmin}",
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
                    "BIOMETRIC LOGIN: Account locked. UserId={UserId}",
                    user.Id);

                return RedirectToPage("./Lockout");
            }

            if (!passwordResult.Succeeded)
            {
                _logger.LogWarning(
                    "BIOMETRIC LOGIN: Invalid password. UserId={UserId}",
                    user.Id);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            _logger.LogInformation(
                "BIOMETRIC LOGIN: Password verified. UserId={UserId}",
                user.Id);

            // ========================================================
            // EMPLOYEE SINGLE DEVICE
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

                _logger.LogInformation(
                    "BIOMETRIC DEVICE: Login device={DeviceId}, UserId={UserId}",
                    deviceId,
                    user.Id);

                var deviceAllowed =
                    await AcquireEmployeeDeviceAsync(
                        user,
                        deviceId);

                if (!deviceAllowed)
                {
                    _logger.LogWarning(
                        "BIOMETRIC DEVICE: LOGIN BLOCKED. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }

                SetDeviceCookie(deviceId);

                _logger.LogInformation(
                    "BIOMETRIC DEVICE: LOCK ACTIVE. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // IDENTITY SIGN-IN
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "BIOMETRIC LOGIN: Authentication successful. UserId={UserId}",
                user.Id);

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
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/"
                });
        }

        // ============================================================
        // SINGLE DEVICE LOCK
        // ============================================================

        private async Task<bool> AcquireEmployeeDeviceAsync(
            IdentityUser user,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            // --------------------------------------------------------
            // PostgreSQL advisory transaction lock
            //
            // This serializes login attempts for the SAME employee.
            // --------------------------------------------------------

            var lockKey =
                CreateStableLockKey(user.Id);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                SELECT pg_advisory_xact_lock({lockKey})
                """);

            _logger.LogInformation(
                "BIOMETRIC DEVICE: Database lock acquired. UserId={UserId}",
                user.Id);

            // --------------------------------------------------------
            // Check existing ActiveDevice
            // --------------------------------------------------------

            var existing =
                await db
                    .Set<IdentityUserToken<string>>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.LoginProvider == LoginProvider &&
                            x.Name == TokenName);

            // --------------------------------------------------------
            // ACTIVE DEVICE EXISTS
            // --------------------------------------------------------

            if (
                existing != null &&
                !string.IsNullOrWhiteSpace(existing.Value))
            {
                _logger.LogWarning(
                    "BIOMETRIC DEVICE: Existing device found. UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                    user.Id,
                    existing.Value,
                    deviceId);

                // Same browser/device is allowed.
                if (
                    string.Equals(
                        existing.Value,
                        deviceId,
                        StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "BIOMETRIC DEVICE: Same device continuing. UserId={UserId}",
                        user.Id);

                    return true;
                }

                // Different device must be blocked.
                _logger.LogWarning(
                    "BIOMETRIC DEVICE: SECOND DEVICE BLOCKED. UserId={UserId}",
                    user.Id);

                return false;
            }

            // --------------------------------------------------------
            // NO ACTIVE DEVICE
            // --------------------------------------------------------

            _logger.LogInformation(
                "BIOMETRIC DEVICE: No active device. Creating lock. UserId={UserId}",
                user.Id);

            var newToken =
                new IdentityUserToken<string>
                {
                    UserId = user.Id,
                    LoginProvider = LoginProvider,
                    Name = TokenName,
                    Value = deviceId
                };

            db.Set<IdentityUserToken<string>>()
                .Add(newToken);

            await db.SaveChangesAsync();

            // --------------------------------------------------------
            // VERIFY DATABASE ROW
            // --------------------------------------------------------

            var verification =
                await db
                    .Set<IdentityUserToken<string>>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.LoginProvider == LoginProvider &&
                            x.Name == TokenName);

            if (
                verification == null ||
                !string.Equals(
                    verification.Value,
                    deviceId,
                    StringComparison.Ordinal))
            {
                _logger.LogError(
                    "BIOMETRIC DEVICE: FAILED TO VERIFY ActiveDevice row. UserId={UserId}",
                    user.Id);

                return false;
            }

            _logger.LogInformation(
                "BIOMETRIC DEVICE: ActiveDevice row VERIFIED. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);

            return true;
        }

        // ============================================================
        // STABLE POSTGRES ADVISORY LOCK KEY
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