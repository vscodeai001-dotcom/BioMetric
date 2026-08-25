using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Payroll.Shared.Data;
using System.ComponentModel.DataAnnotations;
using System.Data;

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

            _logger.LogInformation(
                "LOGIN ROLE CHECK: Email={Email}, UserId={UserId}, Employee={Employee}, Admin={Admin}, SuperAdmin={SuperAdmin}",
                user.Email,
                user.Id,
                isEmployee,
                isAdmin,
                isSuperAdmin);

            // ========================================================
            // VERIFY PASSWORD FIRST
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
            // EMPLOYEE SINGLE DEVICE
            // ========================================================

            if (isEmployeeOnly)
            {
                var deviceId =
                    GetExistingDeviceId();

                var isNewDevice =
                    string.IsNullOrWhiteSpace(deviceId);

                if (isNewDevice)
                {
                    deviceId =
                        Guid.NewGuid().ToString("N");

                    _logger.LogInformation(
                        "NEW EMPLOYEE DEVICE CREATED: UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);
                }
                else
                {
                    _logger.LogInformation(
                        "EXISTING EMPLOYEE DEVICE: UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);
                }

                // ----------------------------------------------------
                // ATOMIC DATABASE LOCK
                // ----------------------------------------------------

                var lockResult =
                    await AcquireEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId!);

                if (!lockResult)
                {
                    _logger.LogWarning(
                        "SECOND DEVICE LOGIN BLOCKED: UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }

                // ----------------------------------------------------
                // Only save browser device cookie AFTER DB lock
                // succeeds.
                // ----------------------------------------------------

                SetDeviceCookie(deviceId!);

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK ACQUIRED: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // CREATE IDENTITY SESSION
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "IDENTITY LOGIN SUCCESS: Email={Email}, UserId={UserId}",
                user.Email,
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
                return LocalRedirect(
                    returnUrl);
            }

            return LocalRedirect("~/");
        }

        // ============================================================
        // GET EXISTING DEVICE COOKIE
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

        // ============================================================
        // SET DEVICE COOKIE
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

                    Secure = true,

                    SameSite =
                        SameSiteMode.Lax,

                    IsEssential = true,

                    MaxAge =
                        TimeSpan.FromDays(365),

                    Path = "/"
                });
        }

        // ============================================================
        // ATOMIC EMPLOYEE DEVICE LOCK
        // ============================================================
        //
        // IMPORTANT:
        //
        // We lock the employee row itself using PostgreSQL
        // SELECT ... FOR UPDATE.
        //
        // Therefore:
        //
        // Login A -> locks employee row
        // Login B -> waits
        // Login A -> creates ActiveDevice
        // Login A -> commits
        // Login B -> checks ActiveDevice
        // Login B -> gets rejected
        //
        // This prevents the "both logins succeeded" race.
        // ============================================================

        private async Task<bool> AcquireEmployeeDeviceLockAsync(
            string userId,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);

            try
            {
                _logger.LogInformation(
                    "DEVICE LOCK START: UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);

                // ====================================================
                // LOCK USER ROW
                // ====================================================

                var connection =
                    (NpgsqlConnection)
                    db.Database.GetDbConnection();

                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                await using (
                    var lockCommand =
                        new NpgsqlCommand(
                            """
                            SELECT "Id"
                            FROM "AspNetUsers"
                            WHERE "Id" = @userId
                            FOR UPDATE
                            """,
                            connection,
                            (NpgsqlTransaction)
                            transaction.GetDbTransaction()))
                {
                    lockCommand.Parameters.AddWithValue(
                        "userId",
                        userId);

                    var result =
                        await lockCommand.ExecuteScalarAsync();

                    if (result == null)
                    {
                        await transaction.RollbackAsync();

                        _logger.LogError(
                            "DEVICE LOCK FAILED: Employee user row not found. UserId={UserId}",
                            userId);

                        return false;
                    }
                }

                // ====================================================
                // NOW CHECK ACTIVE DEVICE
                // ====================================================

                var existingToken =
                    await db
                        .Set<IdentityUserToken<string>>()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId &&
                                x.LoginProvider ==
                                    LoginProvider &&
                                x.Name ==
                                    TokenName);

                // ====================================================
                // EXISTING ACTIVE DEVICE
                // ====================================================

                if (
                    existingToken != null &&
                    !string.IsNullOrWhiteSpace(
                        existingToken.Value))
                {
                    _logger.LogWarning(
                        "ACTIVE DEVICE EXISTS: UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        userId,
                        existingToken.Value,
                        deviceId);

                    // Same browser/device is allowed.
                    if (
                        string.Equals(
                            existingToken.Value,
                            deviceId,
                            StringComparison.Ordinal))
                    {
                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "SAME DEVICE LOGIN ALLOWED: UserId={UserId}",
                            userId);

                        return true;
                    }

                    // Different browser/device is blocked.
                    await transaction.RollbackAsync();

                    _logger.LogWarning(
                        "SECOND DEVICE REJECTED: UserId={UserId}",
                        userId);

                    return false;
                }

                // ====================================================
                // NO ACTIVE DEVICE
                // ====================================================

                _logger.LogInformation(
                    "NO ACTIVE DEVICE: Creating new lock. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);

                var newToken =
                    new IdentityUserToken<string>
                    {
                        UserId = userId,

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
                // VERIFY INSERT
                // ====================================================

                var verification =
                    await db
                        .Set<IdentityUserToken<string>>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId &&
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
                        "DEVICE LOCK INSERT VERIFICATION FAILED: UserId={UserId}",
                        userId);

                    return false;
                }

                // ====================================================
                // COMMIT LOCK
                // ====================================================

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "DEVICE LOCK CREATED: UserId={UserId}, DeviceId={DeviceId}",
                    userId,
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
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Ignore rollback failure.
                }

                _logger.LogWarning(
                    "DEVICE LOCK UNIQUE VIOLATION: UserId={UserId}, DeviceId={DeviceId}",
                    userId,
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
                    userId,
                    deviceId);

                return false;
            }
        }
    }
}