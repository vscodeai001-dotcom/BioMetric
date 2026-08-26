using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payroll.Shared.Data;
using System.ComponentModel.DataAnnotations;

namespace Payroll.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

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
        // FORM
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
        // POST LOGIN
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : "/";

            // --------------------------------------------------------
            // VALIDATION
            // --------------------------------------------------------

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var email = Input.Email.Trim();

            // --------------------------------------------------------
            // FIND USER
            // --------------------------------------------------------

            var user =
                await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

                return Page();
            }

            // --------------------------------------------------------
            // ROLES
            // --------------------------------------------------------

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

            // --------------------------------------------------------
            // PASSWORD
            // --------------------------------------------------------

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);

            if (passwordResult.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }

            if (passwordResult.IsNotAllowed)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This account is currently not allowed to sign in.");

                return Page();
            }

            if (!passwordResult.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

                return Page();
            }

            _logger.LogInformation(
                "PASSWORD VERIFIED. UserId={UserId}",
                user.Id);

            // ========================================================
            // EMPLOYEE SINGLE LOGIN LOCK
            // ========================================================

            string? deviceId = null;
            bool lockCreated = false;

            if (isEmployeeOnly)
            {
                // ----------------------------------------------------
                // IMPORTANT:
                //
                // Every login attempt gets a new device ID.
                //
                // Therefore a second browser/device cannot reuse
                // the first browser's ID to bypass the lock.
                // ----------------------------------------------------

                deviceId =
                    Guid.NewGuid().ToString("N");

                _logger.LogInformation(
                    "LOGIN DEVICE CREATED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                // ----------------------------------------------------
                // ATOMIC DATABASE LOCK
                // ----------------------------------------------------

                lockCreated =
                    await TryCreateEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);

                if (!lockCreated)
                {
                    _logger.LogWarning(
                        "EMPLOYEE LOGIN BLOCKED. ACTIVE LOGIN ALREADY EXISTS. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in. Please log out from the other device before signing in again.");

                    return Page();
                }

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK CREATED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // ASP.NET IDENTITY SIGN-IN
            // ========================================================

            try
            {
                await _signInManager.SignInAsync(
                    user,
                    isPersistent: Input.RememberMe);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IDENTITY SIGN-IN FAILED. UserId={UserId}",
                    user.Id);

                // ----------------------------------------------------
                // If Identity sign-in fails, remove ONLY the lock
                // created by this login attempt.
                // ----------------------------------------------------

                if (
                    isEmployeeOnly &&
                    lockCreated &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
                    await RemoveEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);
                }

                ModelState.AddModelError(
                    string.Empty,
                    "Unable to sign in. Please try again.");

                return Page();
            }

            // ========================================================
            // DEVICE COOKIE
            // ========================================================

            if (
                isEmployeeOnly &&
                lockCreated &&
                !string.IsNullOrWhiteSpace(deviceId))
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

                        // Cookie survives browser restart.
                        // The database lock is still the real lock.
                        MaxAge = TimeSpan.FromDays(365),

                        Path = "/"
                    });
            }

            // ========================================================
            // LOGIN SUCCESS
            // ========================================================

            _logger.LogInformation(
                "LOGIN SUCCESS. UserId={UserId}, Email={Email}, Employee={Employee}, RememberMe={RememberMe}",
                user.Id,
                user.Email,
                isEmployeeOnly,
                Input.RememberMe);

            // ========================================================
            // EMPLOYEE
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect("/employee-home");
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
        // CREATE EMPLOYEE DEVICE LOCK
        //
        // THIS IS THE IMPORTANT PART.
        //
        // PostgreSQL guarantees that only ONE login can insert
        // a row for the same UserId.
        // ============================================================

        private async Task<bool>
            TryCreateEmployeeDeviceLockAsync(
                string userId,
                string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            try
            {
                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection npgsqlConnection)
                {
                    _logger.LogError(
                        "DEVICE LOCK FAILED: PostgreSQL connection unavailable.");

                    return false;
                }

                if (npgsqlConnection.State !=
                    System.Data.ConnectionState.Open)
                {
                    await npgsqlConnection.OpenAsync();
                }

                // ====================================================
                // ATOMIC INSERT
                //
                // If UserId already exists:
                //
                // DO NOTHING
                //
                // If UserId does not exist:
                //
                // INSERT LOCK
                // ====================================================

                const string sql =
                    """
                    INSERT INTO "employee_device_locks"
                    (
                        "Id",
                        "UserId",
                        "DeviceId",
                        "CreatedAtUtc",
                        "LastSeenAtUtc"
                    )
                    VALUES
                    (
                        @id,
                        @userId,
                        @deviceId,
                        @createdAtUtc,
                        @lastSeenAtUtc
                    )
                    ON CONFLICT ("UserId")
                    DO NOTHING
                    RETURNING "Id";
                    """;

                await using var command =
                    new NpgsqlCommand(
                        sql,
                        npgsqlConnection);

                command.Parameters.AddWithValue(
                    "id",
                    Guid.NewGuid());

                command.Parameters.AddWithValue(
                    "userId",
                    userId);

                command.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                command.Parameters.AddWithValue(
                    "createdAtUtc",
                    DateTime.UtcNow);

                command.Parameters.AddWithValue(
                    "lastSeenAtUtc",
                    DateTime.UtcNow);

                var result =
                    await command.ExecuteScalarAsync();

                // ----------------------------------------------------
                // NULL = ROW ALREADY EXISTED
                // ----------------------------------------------------

                if (result == null)
                {
                    _logger.LogWarning(
                        "DEVICE LOCK ALREADY EXISTS. LOGIN BLOCKED. UserId={UserId}",
                        userId);

                    return false;
                }

                _logger.LogInformation(
                    "DEVICE LOCK INSERTED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);

                return true;
            }
            catch (PostgresException ex)
            {
                _logger.LogError(
                    ex,
                    "POSTGRES DEVICE LOCK ERROR. UserId={UserId}",
                    userId);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "DEVICE LOCK ERROR. UserId={UserId}",
                    userId);

                return false;
            }
        }

        // ============================================================
        // REMOVE LOCK AFTER IDENTITY SIGN-IN FAILURE
        // ============================================================

        private async Task
            RemoveEmployeeDeviceLockAsync(
                string userId,
                string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            try
            {
                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection npgsqlConnection)
                {
                    return;
                }

                if (npgsqlConnection.State !=
                    System.Data.ConnectionState.Open)
                {
                    await npgsqlConnection.OpenAsync();
                }

                const string sql =
                    """
                    DELETE FROM "employee_device_locks"
                    WHERE "UserId" = @userId
                      AND "DeviceId" = @deviceId;
                    """;

                await using var command =
                    new NpgsqlCommand(
                        sql,
                        npgsqlConnection);

                command.Parameters.AddWithValue(
                    "userId",
                    userId);

                command.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                await command.ExecuteNonQueryAsync();

                _logger.LogInformation(
                    "DEVICE LOCK REMOVED AFTER SIGN-IN FAILURE. UserId={UserId}",
                    userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "FAILED TO REMOVE DEVICE LOCK AFTER SIGN-IN FAILURE. UserId={UserId}",
                    userId);
            }
        }
    }
}