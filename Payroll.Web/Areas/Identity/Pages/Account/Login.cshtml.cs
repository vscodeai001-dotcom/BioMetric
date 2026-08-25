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
        // ============================================================
        // EMPLOYEE SINGLE-DEVICE LOCK
        // ============================================================

        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private const string LoginProvider =
            "BioMetric";

        private const string TokenName =
            "ActiveDevice";

        // ============================================================
        // DEPENDENCIES
        // ============================================================

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
        // PAGE MODEL
        // ============================================================

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
        // POST LOGIN
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            returnUrl =
                returnUrl ??
                Url.Content("~/") ??
                "/";

            ReturnUrl = returnUrl;

            // ========================================================
            // VALIDATION
            // ========================================================

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
            // ROLE CHECK
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

            _logger.LogWarning(
                "BIOMETRIC LOGIN: Email={Email}, UserId={UserId}, Employee={Employee}, Admin={Admin}, SuperAdmin={SuperAdmin}, EmployeeOnly={EmployeeOnly}",
                user.Email,
                user.Id,
                isEmployee,
                isAdmin,
                isSuperAdmin,
                isEmployeeOnly);

            // ========================================================
            // VERIFY PASSWORD FIRST
            // ========================================================
            //
            // VERY IMPORTANT:
            //
            // Never create an ActiveDevice lock before the password
            // has been successfully verified.
            //
            // Otherwise someone entering a wrong password could
            // accidentally reserve the employee account.
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);

            if (passwordResult.IsLockedOut)
            {
                _logger.LogWarning(
                    "LOGIN LOCKED OUT: Email={Email}",
                    user.Email);

                return RedirectToPage(
                    "./Lockout");
            }

            if (!passwordResult.Succeeded)
            {
                _logger.LogWarning(
                    "LOGIN FAILED: Invalid password. Email={Email}",
                    user.Email);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid login attempt.");

                return Page();
            }

            _logger.LogInformation(
                "PASSWORD VERIFIED: Email={Email}, UserId={UserId}",
                user.Email,
                user.Id);

            // ========================================================
            // EMPLOYEE SINGLE-DEVICE ENFORCEMENT
            // ========================================================

            if (isEmployeeOnly)
            {
                // ----------------------------------------------------
                // Get existing browser/device ID
                // ----------------------------------------------------

                var deviceId =
                    GetExistingDeviceId();

                // ----------------------------------------------------
                // New browser/device
                // ----------------------------------------------------

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId =
                        Guid.NewGuid().ToString("N");

                    _logger.LogInformation(
                        "NEW DEVICE GENERATED: UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);
                }
                else
                {
                    _logger.LogInformation(
                        "EXISTING DEVICE FOUND: UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);
                }

                // ----------------------------------------------------
                // Try to acquire account lock
                // ----------------------------------------------------

                var acquired =
                    await TryAcquireEmployeeDeviceAsync(
                        user,
                        deviceId);

                _logger.LogWarning(
                    "DEVICE LOCK RESULT: UserId={UserId}, DeviceId={DeviceId}, Acquired={Acquired}",
                    user.Id,
                    deviceId,
                    acquired);

                // ----------------------------------------------------
                // Another device already owns the account
                // ----------------------------------------------------

                if (!acquired)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }

                // ----------------------------------------------------
                // Lock successfully acquired
                // ----------------------------------------------------

                SetDeviceCookie(deviceId);

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK ACTIVE: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // CREATE ASP.NET CORE IDENTITY SESSION
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "IDENTITY LOGIN SUCCESS: Email={Email}, UserId={UserId}",
                user.Email,
                user.Id);

            // ========================================================
            // EMPLOYEE REDIRECT
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect(
                    "~/employee-home");
            }

            // ========================================================
            // ADMIN / SUPERADMIN RETURN URL
            // ========================================================

            if (
                Url.IsLocalUrl(returnUrl) &&
                returnUrl != "/")
            {
                return LocalRedirect(
                    returnUrl);
            }

            // ========================================================
            // DEFAULT ADMIN / SUPERADMIN
            // ========================================================

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

                    SameSite =
                        SameSiteMode.Lax,

                    IsEssential = true,

                    MaxAge =
                        TimeSpan.FromDays(365),

                    Path = "/"
                });
        }

        // ============================================================
        // EMPLOYEE DEVICE LOCK
        // ============================================================

        private async Task<bool>
            TryAcquireEmployeeDeviceAsync(
                IdentityUser user,
                string deviceId)
        {
            await using var db =
                await _dbFactory
                    .CreateDbContextAsync();

            // ========================================================
            // SERIALIZABLE TRANSACTION
            // ========================================================
            //
            // Prevents two simultaneous login requests from both
            // believing the account is free.
            // ========================================================

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                _logger.LogInformation(
                    "DEVICE LOCK CHECK: UserId={UserId}",
                    user.Id);

                // ====================================================
                // LOOK FOR ACTIVE DEVICE
                // ====================================================

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
                // ACTIVE DEVICE EXISTS
                // ====================================================

                if (
                    token != null &&
                    !string.IsNullOrWhiteSpace(
                        token.Value))
                {
                    _logger.LogWarning(
                        "ACTIVE DEVICE FOUND: UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        user.Id,
                        token.Value,
                        deviceId);

                    // ------------------------------------------------
                    // SAME DEVICE
                    // ------------------------------------------------

                    if (
                        string.Equals(
                            token.Value,
                            deviceId,
                            StringComparison.Ordinal))
                    {
                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "SAME DEVICE LOGIN ALLOWED: UserId={UserId}",
                            user.Id);

                        return true;
                    }

                    // ------------------------------------------------
                    // DIFFERENT DEVICE
                    // ------------------------------------------------

                    await transaction.RollbackAsync();

                    _logger.LogWarning(
                        "SECOND DEVICE LOGIN BLOCKED: UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        user.Id,
                        token.Value,
                        deviceId);

                    return false;
                }

                // ====================================================
                // NO ACTIVE DEVICE
                // ====================================================

                _logger.LogInformation(
                    "NO ACTIVE DEVICE FOUND: Creating lock. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                var newToken =
                    new IdentityUserToken<string>
                    {
                        UserId =
                            user.Id,

                        LoginProvider =
                            LoginProvider,

                        Name =
                            TokenName,

                        Value =
                            deviceId
                    };

                db.Set<IdentityUserToken<string>>()
                    .Add(newToken);

                await db.SaveChangesAsync();

                // ====================================================
                // VERIFY THAT THE ROW REALLY EXISTS
                // ====================================================

                var verification =
                    await db
                        .Set<IdentityUserToken<string>>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == user.Id &&
                                x.LoginProvider ==
                                    LoginProvider &&
                                x.Name ==
                                    TokenName);

                if (
                    verification == null ||
                    verification.Value != deviceId)
                {
                    await transaction.RollbackAsync();

                    _logger.LogError(
                        "DEVICE LOCK VERIFICATION FAILED: UserId={UserId}",
                        user.Id);

                    return false;
                }

                // ====================================================
                // COMMIT
                // ====================================================

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "DEVICE LOCK CREATED SUCCESSFULLY: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                return true;
            }
            catch (DbUpdateException ex)
                when (
                    ex.InnerException
                        is PostgresException pg &&
                    pg.SqlState ==
                        PostgresErrorCodes.UniqueViolation)
            {
                // ====================================================
                // CONCURRENT LOGIN
                // ====================================================
                //
                // Another device inserted the token at exactly the
                // same time. PostgreSQL's unique key allows only one.
                // This device loses the race.
                // ====================================================

                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Ignore rollback failure.
                }

                _logger.LogWarning(
                    "CONCURRENT LOGIN BLOCKED: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

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
                    "DEVICE LOCK ERROR: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                throw;
            }
        }
    }
}