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
        // EMPLOYEE SINGLE DEVICE
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
        // FORM MODEL
        // ============================================================

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = "/";

        [TempData]
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Email is required.")]
            [EmailAddress(ErrorMessage = "Enter a valid email address.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required.")]
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
            ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : "/";

            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                ModelState.AddModelError(
                    string.Empty,
                    ErrorMessage);
            }
        }

        // ============================================================
        // POST
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : "/";

            // ========================================================
            // VALIDATE FORM
            // ========================================================

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // ========================================================
            // FIND USER
            // ========================================================

            var email =
                Input.Email.Trim();

            var user =
                await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                _logger.LogWarning(
                    "LOGIN FAILED: User not found. Email={Email}",
                    email);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

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
                "LOGIN ROLE CHECK: UserId={UserId}, Email={Email}, Employee={Employee}, Admin={Admin}, SuperAdmin={SuperAdmin}",
                user.Id,
                user.Email,
                isEmployee,
                isAdmin,
                isSuperAdmin);

            // ========================================================
            // PASSWORD CHECK
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);

            if (passwordResult.IsLockedOut)
            {
                _logger.LogWarning(
                    "LOGIN LOCKED OUT: UserId={UserId}, Email={Email}",
                    user.Id,
                    user.Email);

                return RedirectToPage("./Lockout");
            }

            if (passwordResult.IsNotAllowed)
            {
                _logger.LogWarning(
                    "LOGIN NOT ALLOWED: UserId={UserId}, Email={Email}",
                    user.Id,
                    user.Email);

                ModelState.AddModelError(
                    string.Empty,
                    "This account is currently not allowed to sign in.");

                return Page();
            }

            if (!passwordResult.Succeeded)
            {
                _logger.LogWarning(
                    "LOGIN FAILED: Invalid password. UserId={UserId}, Email={Email}",
                    user.Id,
                    user.Email);

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

                return Page();
            }

            _logger.LogInformation(
                "PASSWORD VERIFIED: UserId={UserId}, Email={Email}",
                user.Id,
                user.Email);

            // ========================================================
            // EMPLOYEE SINGLE DEVICE LOCK
            // ========================================================

            if (isEmployeeOnly)
            {
                var existingDeviceId =
                    GetExistingDeviceId();

                var deviceId =
                    existingDeviceId;

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId =
                        Guid.NewGuid().ToString("N");

                    _logger.LogInformation(
                        "NEW EMPLOYEE DEVICE: UserId={UserId}, DeviceId={DeviceId}",
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
                // ATOMIC LOCK
                // ----------------------------------------------------

                var lockAcquired =
                    await AcquireEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);

                if (!lockAcquired)
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
                // SAVE DEVICE COOKIE ONLY AFTER LOCK SUCCEEDS
                // ----------------------------------------------------

                SetDeviceCookie(deviceId);

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK ACQUIRED: UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                _logger.LogWarning(
    "========== BIOMETRIC LOCK CONFIRMED ==========");
                _logger.LogWarning(
                    "UserId={UserId}", user.Id);
                _logger.LogWarning(
                    "DeviceId={DeviceId}", deviceId);
                _logger.LogWarning(
                    "========== BIOMETRIC LOCK CONFIRMED ==========");
            }

            // ========================================================
            // CREATE IDENTITY AUTHENTICATION COOKIE
            // ========================================================

            await _signInManager.SignInAsync(
                user,
                isPersistent: Input.RememberMe);

            _logger.LogInformation(
                "LOGIN SUCCESS: UserId={UserId}, Email={Email}, RememberMe={RememberMe}",
                user.Id,
                user.Email,
                Input.RememberMe);

            // ========================================================
            // EMPLOYEE HOME
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect(
                    "/employee-home");
            }

            // ========================================================
            // ADMIN / SUPERADMIN
            // ========================================================

            if (
                !string.IsNullOrWhiteSpace(ReturnUrl) &&
                ReturnUrl != "/" &&
                Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return LocalRedirect("/");
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
                return deviceId.Trim();
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

                    SameSite = SameSiteMode.Lax,

                    IsEssential = true,

                    MaxAge = TimeSpan.FromDays(365),

                    Path = "/"
                });
        }

        // ============================================================
        // ATOMIC EMPLOYEE DEVICE LOCK
        // ============================================================
        //
        // PostgreSQL row lock guarantees:
        //
        // Login A -> locks employee row
        // Login B -> waits
        // Login A -> creates ActiveDevice
        // Login A -> commits
        // Login B -> sees ActiveDevice
        // Login B -> rejected
        //
        // Therefore two different devices cannot successfully
        // acquire the employee lock at the same time.
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
                // GET CONNECTION
                // ====================================================

                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection npgsqlConnection)
                {
                    _logger.LogError(
                        "DEVICE LOCK FAILED: Database connection is not PostgreSQL. UserId={UserId}",
                        userId);

                    await transaction.RollbackAsync();

                    return false;
                }

                if (
                    npgsqlConnection.State !=
                    ConnectionState.Open)
                {
                    await npgsqlConnection.OpenAsync();
                }

                // ====================================================
                // LOCK EMPLOYEE USER ROW
                // ====================================================

                await using (
                    var lockCommand =
                        new NpgsqlCommand(
                            """
                            SELECT "Id"
                            FROM "AspNetUsers"
                            WHERE "Id" = @userId
                            FOR UPDATE
                            """,
                            npgsqlConnection,
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
                        _logger.LogError(
                            "DEVICE LOCK FAILED: User row not found. UserId={UserId}",
                            userId);

                        await transaction.RollbackAsync();

                        return false;
                    }
                }

                // ====================================================
                // CHECK ACTIVE DEVICE
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
                // ACTIVE DEVICE EXISTS
                // ====================================================

                if (
                    existingToken != null &&
                    !string.IsNullOrWhiteSpace(
                        existingToken.Value))
                {
                    var existingValue =
                        existingToken.Value.Trim();

                    // ------------------------------------------------
                    // SAME DEVICE
                    // ------------------------------------------------

                    if (
                        string.Equals(
                            existingValue,
                            deviceId,
                            StringComparison.Ordinal))
                    {
                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "SAME DEVICE LOGIN ALLOWED: UserId={UserId}",
                            userId);

                        return true;
                    }

                    // ------------------------------------------------
                    // DIFFERENT DEVICE
                    // ------------------------------------------------

                    _logger.LogWarning(
                        "SECOND DEVICE REJECTED: UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        userId,
                        existingValue,
                        deviceId);

                    await transaction.RollbackAsync();

                    return false;
                }

                // ====================================================
                // NO ACTIVE DEVICE
                // ====================================================

                var newToken =
                    new IdentityUserToken<string>
                    {
                        UserId =
                            userId,

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
                // VERIFY TOKEN BEFORE COMMIT
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
                    !string.Equals(
                        verification.Value,
                        deviceId,
                        StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "DEVICE LOCK VERIFICATION FAILED: UserId={UserId}",
                        userId);

                    await transaction.RollbackAsync();

                    return false;
                }

                // ====================================================
                // COMMIT
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