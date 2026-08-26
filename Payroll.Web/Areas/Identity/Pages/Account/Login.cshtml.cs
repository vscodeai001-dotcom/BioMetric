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

        private const string DeviceLockTable =
            "employee_device_locks";

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
                "PASSWORD VERIFIED. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}",
                user.Id,
                user.Email,
                isEmployeeOnly);

            // ========================================================
            // EMPLOYEE SINGLE-LOGIN LOCK
            // ========================================================

            string? deviceId = null;
            bool lockCreated = false;

            if (isEmployeeOnly)
            {
                deviceId =
                    Guid.NewGuid().ToString("N");

                _logger.LogWarning(
                    "========== EMPLOYEE DEVICE LOCK ATTEMPT ==========");

                _logger.LogWarning(
                    "UserId={UserId}",
                    user.Id);

                _logger.LogWarning(
                    "Email={Email}",
                    user.Email);

                _logger.LogWarning(
                    "DeviceId={DeviceId}",
                    deviceId);

                // ----------------------------------------------------
                // CREATE LOCK
                // ----------------------------------------------------

                lockCreated =
                    await TryCreateEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);

                // ----------------------------------------------------
                // LOCK FAILED
                // ----------------------------------------------------

                if (!lockCreated)
                {
                    _logger.LogWarning(
                        "========== EMPLOYEE LOGIN BLOCKED ==========");

                    _logger.LogWarning(
                        "UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in. Please log out from the other device before signing in again.");

                    return Page();
                }

                _logger.LogWarning(
                    "========== EMPLOYEE DEVICE LOCK CONFIRMED ==========");

                _logger.LogWarning(
                    "UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // IDENTITY SIGN-IN
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
                // REMOVE ONLY OUR LOCK
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
                        MaxAge = TimeSpan.FromDays(365),
                        Path = "/"
                    });

                _logger.LogInformation(
                    "EMPLOYEE DEVICE COOKIE CREATED. UserId={UserId}",
                    user.Id);
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
                        "DEVICE LOCK FAILED: Database connection is not PostgreSQL.");

                    return false;
                }

                if (npgsqlConnection.State !=
                    System.Data.ConnectionState.Open)
                {
                    await npgsqlConnection.OpenAsync();
                }

                // ====================================================
                // VERIFY DATABASE
                // ====================================================

                const string databaseCheckSql =
                    """
                    SELECT
                        current_database(),
                        current_schema();
                    """;

                await using (
                    var databaseCheckCommand =
                        new NpgsqlCommand(
                            databaseCheckSql,
                            npgsqlConnection))
                {
                    await using var reader =
                        await databaseCheckCommand.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        var databaseName =
                            reader.GetString(0);

                        var schemaName =
                            reader.GetString(1);

                        _logger.LogWarning(
                            "DEVICE LOCK DATABASE = {Database}, SCHEMA = {Schema}",
                            databaseName,
                            schemaName);
                    }
                }

                // ====================================================
                // FIRST CHECK
                //
                // This makes the intended behavior explicit.
                // ====================================================

                const string existingLockSql =
                    """
                    SELECT "DeviceId"
                    FROM public."employee_device_locks"
                    WHERE "UserId" = @userId
                    LIMIT 1;
                    """;

                await using (
                    var existingCommand =
                        new NpgsqlCommand(
                            existingLockSql,
                            npgsqlConnection))
                {
                    existingCommand.Parameters.AddWithValue(
                        "userId",
                        userId);

                    var existingDevice =
                        await existingCommand.ExecuteScalarAsync();

                    if (existingDevice != null)
                    {
                        _logger.LogWarning(
                            "DEVICE LOCK ALREADY EXISTS. UserId={UserId}, ExistingDevice={ExistingDevice}",
                            userId,
                            existingDevice);

                        return false;
                    }
                }

                // ====================================================
                // ATOMIC INSERT
                //
                // UNIQUE UserId constraint guarantees that two
                // simultaneous requests cannot both create a lock.
                // ====================================================

                const string insertSql =
                    """
                    INSERT INTO public."employee_device_locks"
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

                await using var insertCommand =
                    new NpgsqlCommand(
                        insertSql,
                        npgsqlConnection);

                insertCommand.Parameters.AddWithValue(
                    "id",
                    Guid.NewGuid());

                insertCommand.Parameters.AddWithValue(
                    "userId",
                    userId);

                insertCommand.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                var now =
                    DateTime.UtcNow;

                insertCommand.Parameters.AddWithValue(
                    "createdAtUtc",
                    now);

                insertCommand.Parameters.AddWithValue(
                    "lastSeenAtUtc",
                    now);

                var insertedId =
                    await insertCommand.ExecuteScalarAsync();

                // ====================================================
                // INSERT DID NOT HAPPEN
                // ====================================================

                if (insertedId == null)
                {
                    _logger.LogWarning(
                        "DEVICE LOCK INSERT SKIPPED. UserId={UserId}",
                        userId);

                    return false;
                }

                // ====================================================
                // CRITICAL VERIFICATION
                //
                // We immediately read the row back.
                //
                // Login is allowed ONLY if the row really exists.
                // ====================================================

                const string verifySql =
                    """
                    SELECT
                        "Id",
                        "UserId",
                        "DeviceId"
                    FROM public."employee_device_locks"
                    WHERE "UserId" = @userId
                      AND "DeviceId" = @deviceId
                    LIMIT 1;
                    """;

                await using var verifyCommand =
                    new NpgsqlCommand(
                        verifySql,
                        npgsqlConnection);

                verifyCommand.Parameters.AddWithValue(
                    "userId",
                    userId);

                verifyCommand.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                await using var verifyReader =
                    await verifyCommand.ExecuteReaderAsync();

                if (!await verifyReader.ReadAsync())
                {
                    _logger.LogError(
                        "CRITICAL: DEVICE LOCK INSERTED BUT COULD NOT BE VERIFIED. LOGIN WILL BE BLOCKED. UserId={UserId}",
                        userId);

                    await verifyReader.CloseAsync();

                    await RemoveEmployeeDeviceLockAsync(
                        userId,
                        deviceId);

                    return false;
                }

                var verifiedUserId =
                    verifyReader.GetString(1);

                var verifiedDeviceId =
                    verifyReader.GetString(2);

                await verifyReader.CloseAsync();

                // ====================================================
                // FINAL VERIFICATION
                // ====================================================

                if (
                    !string.Equals(
                        verifiedUserId,
                        userId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        verifiedDeviceId,
                        deviceId,
                        StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "CRITICAL: DEVICE LOCK VERIFICATION FAILED. LOGIN BLOCKED. UserId={UserId}",
                        userId);

                    await RemoveEmployeeDeviceLockAsync(
                        userId,
                        deviceId);

                    return false;
                }

                _logger.LogWarning(
                    "========== DEVICE LOCK SUCCESSFULLY VERIFIED ==========");

                _logger.LogWarning(
                    "UserId={UserId}",
                    userId);

                _logger.LogWarning(
                    "DeviceId={DeviceId}",
                    deviceId);

                return true;
            }
            catch (PostgresException ex)
            {
                _logger.LogError(
                    ex,
                    "POSTGRES DEVICE LOCK ERROR. UserId={UserId}, SqlState={SqlState}",
                    userId,
                    ex.SqlState);

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
        // REMOVE DEVICE LOCK
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
                    DELETE FROM public."employee_device_locks"
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

                var deleted =
                    await command.ExecuteNonQueryAsync();

                _logger.LogInformation(
                    "DEVICE LOCK CLEANUP. UserId={UserId}, DeletedRows={DeletedRows}",
                    userId,
                    deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "FAILED TO REMOVE DEVICE LOCK. UserId={UserId}",
                    userId);
            }
        }
    }
}