using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payroll.Shared.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Payroll.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        // ============================================================
        // CONSTANTS
        // ============================================================

        public const string DeviceCookieName =
            "BioMetric-Employee-Device";

        public const string DeviceClaimType =
            "BioMetric-Employee-Device";

        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<LoginModel> _logger;

        // ============================================================
        // CONSTRUCTOR
        // ============================================================

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

        // ============================================================
        // INPUT MODEL
        // ============================================================

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
                AddLoginError();
                return Page();
            }

            // --------------------------------------------------------
            // ROLE
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
                AddLoginError();
                return Page();
            }

            _logger.LogInformation(
                "LOGIN PASSWORD VERIFIED. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}",
                user.Id,
                user.Email,
                isEmployeeOnly);

            // ========================================================
            // EMPLOYEE SINGLE-SESSION CONTROL
            // ========================================================

            string? deviceId = null;

            if (isEmployeeOnly)
            {
                deviceId =
                    Guid.NewGuid().ToString("N");

                _logger.LogInformation(
                    "EMPLOYEE LOGIN LOCK ATTEMPT. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                var lockCreated =
                    await TryAcquireEmployeeLockAsync(
                        user.Id,
                        deviceId);

                // ----------------------------------------------------
                // SECOND DEVICE / EXISTING LOGIN
                // ----------------------------------------------------

                if (!lockCreated)
                {
                    _logger.LogWarning(
                        "EMPLOYEE LOGIN BLOCKED. ACCOUNT ALREADY ACTIVE. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from that device before logging in here.");

                    return Page();
                }

                _logger.LogInformation(
                    "EMPLOYEE LOCK ACQUIRED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }

            // ========================================================
            // SIGN IN
            // ========================================================

            try
            {
                if (isEmployeeOnly &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
                    // ------------------------------------------------
                    // CRITICAL:
                    // Device ID is stored INSIDE the Identity cookie.
                    // ------------------------------------------------

                    var claims =
                        new[]
                        {
                            new Claim(
                                DeviceClaimType,
                                deviceId)
                        };

                    await _signInManager.SignInWithClaimsAsync(
                        user,
                        Input.RememberMe,
                        claims);
                }
                else
                {
                    await _signInManager.SignInAsync(
                        user,
                        Input.RememberMe);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IDENTITY SIGN-IN FAILED. UserId={UserId}",
                    user.Id);

                if (isEmployeeOnly &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
                    await RemoveEmployeeLockAsync(
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

            if (isEmployeeOnly &&
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
            }

            // ========================================================
            // SUCCESS
            // ========================================================

            _logger.LogInformation(
                "LOGIN SUCCESS. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}",
                user.Id,
                user.Email,
                isEmployeeOnly);

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

            if (!string.IsNullOrWhiteSpace(ReturnUrl) &&
                ReturnUrl != "/" &&
                Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return LocalRedirect("/");
        }

        // ============================================================
        // ATOMIC EMPLOYEE LOCK
        // ============================================================

        private async Task<bool> TryAcquireEmployeeLockAsync(
            string userId,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            try
            {
                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection postgres)
                {
                    _logger.LogError(
                        "EMPLOYEE LOCK FAILED. Database is not PostgreSQL.");

                    return false;
                }

                if (postgres.State !=
                    System.Data.ConnectionState.Open)
                {
                    await postgres.OpenAsync();
                }

                // ----------------------------------------------------
                // IMPORTANT:
                //
                // DO NOT perform a separate SELECT before INSERT.
                //
                // PostgreSQL unique constraint is the authority.
                // ----------------------------------------------------

                const string sql =
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

                await using var command =
                    new NpgsqlCommand(
                        sql,
                        postgres);

                command.Parameters.AddWithValue(
                    "id",
                    Guid.NewGuid());

                command.Parameters.AddWithValue(
                    "userId",
                    userId);

                command.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                var now =
                    DateTime.UtcNow;

                command.Parameters.AddWithValue(
                    "createdAtUtc",
                    now);

                command.Parameters.AddWithValue(
                    "lastSeenAtUtc",
                    now);

                var result =
                    await command.ExecuteScalarAsync();

                // ----------------------------------------------------
                // NULL = ANOTHER DEVICE ALREADY OWNS THE LOCK
                // ----------------------------------------------------

                if (result == null)
                {
                    _logger.LogWarning(
                        "EMPLOYEE LOCK DENIED BY DATABASE UNIQUE CONSTRAINT. UserId={UserId}",
                        userId);

                    return false;
                }

                _logger.LogInformation(
                    "EMPLOYEE LOCK CREATED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);

                return true;
            }
            catch (PostgresException ex)
            {
                _logger.LogError(
                    ex,
                    "POSTGRES EMPLOYEE LOCK ERROR. UserId={UserId}, SqlState={SqlState}",
                    userId,
                    ex.SqlState);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE LOCK ERROR. UserId={UserId}",
                    userId);

                return false;
            }
        }

        // ============================================================
        // REMOVE LOCK
        // ============================================================

        private async Task RemoveEmployeeLockAsync(
            string userId,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            try
            {
                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection postgres)
                {
                    return;
                }

                if (postgres.State !=
                    System.Data.ConnectionState.Open)
                {
                    await postgres.OpenAsync();
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
                        postgres);

                command.Parameters.AddWithValue(
                    "userId",
                    userId);

                command.Parameters.AddWithValue(
                    "deviceId",
                    deviceId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE LOCK CLEANUP FAILED. UserId={UserId}",
                    userId);
            }
        }

        // ============================================================
        // INVALID LOGIN
        // ============================================================

        private void AddLoginError()
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid email or password.");
        }
    }
}