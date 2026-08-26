using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;

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
        // CONSTANTS
        // ============================================================

        public const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private const string DeviceClaimType =
            "BioMetric-Employee-Device";

        private const string InvalidLoginMessage =
            "Invalid email or password.";

        private const string AlreadyLoggedInMessage =
            "This employee is already logged in on another device or browser.";

        private const string ForceLogoutMessage =
            "The existing session has been ended. You can now continue on this device.";


        // ============================================================
        // SERVICES
        // ============================================================

        private readonly SignInManager<IdentityUser>
            _signInManager;

        private readonly UserManager<IdentityUser>
            _userManager;

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        private readonly ILogger<LoginModel>
            _logger;


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


        [BindProperty]
        public bool ForceLogin { get; set; }


        public string ReturnUrl { get; set; } = "/";


        // ============================================================
        // UI STATE
        // ============================================================

        public bool ExistingSessionDetected { get; private set; }

        public DateTime? ExistingSessionLastSeenUtc { get; private set; }


        // ============================================================
        // INPUT MODEL
        // ============================================================

        public class InputModel
        {
            [Required(
                ErrorMessage = "Email is required.")]
            [EmailAddress(
                ErrorMessage = "Enter a valid email address.")]
            public string Email { get; set; } = string.Empty;


            [Required(
                ErrorMessage = "Password is required.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;


            [Display(
                Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }


        // ============================================================
        // GET
        // ============================================================

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl =
                string.IsNullOrWhiteSpace(returnUrl)
                    ? "/"
                    : returnUrl;
        }


        // ============================================================
        // POST
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            ReturnUrl =
                string.IsNullOrWhiteSpace(returnUrl)
                    ? "/"
                    : returnUrl;


            // ========================================================
            // VALIDATE FORM
            // ========================================================

            if (!ModelState.IsValid)
            {
                return Page();
            }


            // ========================================================
            // NORMALIZE EMAIL
            // ========================================================

            var email =
                Input.Email.Trim();


            // ========================================================
            // FIND USER
            // ========================================================

            var user =
                await _userManager.FindByEmailAsync(email);


            if (user == null)
            {
                AddInvalidLoginError();
                return Page();
            }


            // ========================================================
            // ACCOUNT CONFIRMATION
            // ========================================================

            if (
                !await _userManager.IsEmailConfirmedAsync(user) &&
                _userManager.Options.SignIn.RequireConfirmedEmail)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Please confirm your email address before signing in.");

                return Page();
            }


            // ========================================================
            // VERIFY PASSWORD FIRST
            //
            // IMPORTANT:
            //
            // We never reveal whether an account is already logged in
            // until the password has been verified.
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: true);


            if (passwordResult.IsLockedOut)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This account is temporarily locked because of repeated failed login attempts.");

                return Page();
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
                AddInvalidLoginError();
                return Page();
            }


            _logger.LogInformation(
                "LOGIN PASSWORD VERIFIED. UserId={UserId}, Email={Email}",
                user.Id,
                user.Email);


            // ========================================================
            // DETERMINE ROLES
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
            // EMPLOYEE SINGLE SESSION
            // ========================================================

            string? deviceId = null;


            if (isEmployeeOnly)
            {
                // ----------------------------------------------------
                // CHECK EXISTING SESSION
                // ----------------------------------------------------

                var existingLock =
                    await GetEmployeeLockAsync(user.Id);


                if (existingLock != null)
                {
                    ExistingSessionDetected = true;

                    ExistingSessionLastSeenUtc =
                        existingLock.LastSeenAtUtc;


                    _logger.LogWarning(
                        "EMPLOYEE SECOND LOGIN DETECTED. UserId={UserId}, ExistingDeviceId={DeviceId}, LastSeenUtc={LastSeenUtc}",
                        user.Id,
                        existingLock.DeviceId,
                        existingLock.LastSeenAtUtc);


                    // ------------------------------------------------
                    // NORMAL SECOND LOGIN
                    //
                    // Show the user a clear explanation.
                    // ------------------------------------------------

                    if (!ForceLogin)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            AlreadyLoggedInMessage);

                        return Page();
                    }


                    // ------------------------------------------------
                    // FORCE EXISTING SESSION OUT
                    // ------------------------------------------------

                    var forceResult =
                        await ForceLogoutExistingSessionAsync(
                            user);


                    if (!forceResult)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            "We could not end the existing session safely. Please try again.");

                        return Page();
                    }


                    _logger.LogInformation(
                        "EXISTING EMPLOYEE SESSION FORCE LOGGED OUT. UserId={UserId}",
                        user.Id);
                }


                // ----------------------------------------------------
                // CREATE NEW DEVICE ID
                // ----------------------------------------------------

                deviceId =
                    Guid.NewGuid().ToString("N");


                // ----------------------------------------------------
                // ACQUIRE NEW LOCK
                //
                // PostgreSQL UNIQUE(UserId) guarantees that only
                // one request can win.
                // ----------------------------------------------------

                var lockAcquired =
                    await TryAcquireEmployeeLockAsync(
                        user.Id,
                        deviceId);


                if (!lockAcquired)
                {
                    _logger.LogWarning(
                        "EMPLOYEE LOGIN LOST LOCK RACE. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "Another login became active before this login completed. Please try again.");

                    return Page();
                }
            }


            // ========================================================
            // SIGN IN
            // ========================================================

            try
            {
                if (
                    isEmployeeOnly &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
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


                if (
                    isEmployeeOnly &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
                    await RemoveEmployeeLockAsync(
                        user.Id,
                        deviceId);
                }


                ModelState.AddModelError(
                    string.Empty,
                    "Unable to complete sign in. Please try again.");

                return Page();
            }


            // ========================================================
            // DEVICE COOKIE
            // ========================================================

            if (
                isEmployeeOnly &&
                !string.IsNullOrWhiteSpace(deviceId))
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


            // ========================================================
            // SUCCESS
            // ========================================================

            _logger.LogInformation(
                "LOGIN SUCCESS. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}, DeviceId={DeviceId}",
                user.Id,
                user.Email,
                isEmployeeOnly,
                deviceId);


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
        // GET EXISTING LOCK
        // ============================================================

        private async Task<EmployeeDeviceLock?>
            GetEmployeeLockAsync(
                string userId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();


            return await db.EmployeeDeviceLocks
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId);
        }


        // ============================================================
        // FORCE LOGOUT EXISTING SESSION
        // ============================================================
        //
        // IMPORTANT:
        //
        // 1. Verify password already happened.
        // 2. Change Identity security stamp.
        // 3. Remove old device lock.
        //
        // Changing the security stamp invalidates previously issued
        // Identity cookies when the security stamp is validated.
        // ============================================================

        private async Task<bool>
            ForceLogoutExistingSessionAsync(
                IdentityUser user)
        {
            try
            {
                // ----------------------------------------------------
                // INVALIDATE ALL EXISTING IDENTITY COOKIES
                // ----------------------------------------------------

                var stampResult =
                    await _userManager.UpdateSecurityStampAsync(
                        user);


                if (!stampResult.Succeeded)
                {
                    _logger.LogError(
                        "SECURITY STAMP UPDATE FAILED. UserId={UserId}, Errors={Errors}",
                        user.Id,
                        string.Join(
                            "; ",
                            stampResult.Errors.Select(
                                x => x.Description)));

                    return false;
                }


                // ----------------------------------------------------
                // REMOVE EXISTING DEVICE LOCK
                // ----------------------------------------------------

                await using var db =
                    await _dbFactory.CreateDbContextAsync();


                var existingLock =
                    await db.EmployeeDeviceLocks
                        .FirstOrDefaultAsync(
                            x => x.UserId == user.Id);


                if (existingLock != null)
                {
                    db.EmployeeDeviceLocks.Remove(
                        existingLock);

                    await db.SaveChangesAsync();
                }


                _logger.LogInformation(
                    "FORCE LOGOUT COMPLETED. UserId={UserId}",
                    user.Id);


                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "FORCE LOGOUT FAILED. UserId={UserId}",
                    user.Id);

                return false;
            }
        }


        // ============================================================
        // ATOMIC DEVICE LOCK
        // ============================================================

        private async Task<bool>
            TryAcquireEmployeeLockAsync(
                string userId,
                string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();


            var connection =
                db.Database.GetDbConnection();


            if (connection is not NpgsqlConnection postgres)
            {
                throw new InvalidOperationException(
                    "Employee device locking requires PostgreSQL.");
            }


            if (postgres.State != ConnectionState.Open)
            {
                await postgres.OpenAsync();
            }


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


            if (
                result == null ||
                result == DBNull.Value)
            {
                return false;
            }


            return true;
        }


        // ============================================================
        // REMOVE LOCK
        // ============================================================

        private async Task
            RemoveEmployeeLockAsync(
                string userId,
                string deviceId)
        {
            try
            {
                await using var db =
                    await _dbFactory.CreateDbContextAsync();


                var lockRecord =
                    await db.EmployeeDeviceLocks
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId &&
                                x.DeviceId == deviceId);


                if (lockRecord == null)
                {
                    return;
                }


                db.EmployeeDeviceLocks.Remove(
                    lockRecord);


                await db.SaveChangesAsync();


                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK REMOVED AFTER LOGIN FAILURE. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "FAILED TO CLEAN EMPLOYEE DEVICE LOCK. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
        }


        // ============================================================
        // INVALID LOGIN
        // ============================================================

        private void AddInvalidLoginError()
        {
            ModelState.AddModelError(
                string.Empty,
                InvalidLoginMessage);
        }
    }
}