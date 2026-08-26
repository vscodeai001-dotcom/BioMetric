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

        public const string DeviceClaimType =
            "BioMetric-Employee-Device";

        private const string AlreadyLoggedInMessage =
            "This employee account is already logged in on another device or browser. Please log out from the active session before logging in here.";

        private const string InvalidLoginMessage =
            "Invalid email or password.";


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
            _signInManager =
                signInManager;

            _userManager =
                userManager;

            _dbFactory =
                dbFactory;

            _logger =
                logger;
        }


        // ============================================================
        // INPUT
        // ============================================================

        [BindProperty]
        public InputModel Input
        {
            get;
            set;
        } = new();


        public string ReturnUrl
        {
            get;
            set;
        } = "/";


        [TempData]
        public string? ErrorMessage
        {
            get;
            set;
        }


        // ============================================================
        // INPUT MODEL
        // ============================================================

        public class InputModel
        {
            [Required(
                ErrorMessage =
                    "Email is required.")]
            [EmailAddress(
                ErrorMessage =
                    "Enter a valid email address.")]
            public string Email
            {
                get;
                set;
            } = string.Empty;


            [Required(
                ErrorMessage =
                    "Password is required.")]
            [DataType(
                DataType.Password)]
            public string Password
            {
                get;
                set;
            } = string.Empty;


            [Display(
                Name = "Remember me?")]
            public bool RememberMe
            {
                get;
                set;
            }
        }


        // ============================================================
        // GET
        // ============================================================

        public void OnGet(
            string? returnUrl = null)
        {
            ReturnUrl =
                !string.IsNullOrWhiteSpace(
                    returnUrl)
                    ? returnUrl
                    : "/";


            if (!string.IsNullOrWhiteSpace(
                    ErrorMessage))
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
                !string.IsNullOrWhiteSpace(
                    returnUrl)
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
            // NORMALIZE EMAIL
            // ========================================================

            var email =
                Input.Email.Trim();


            // ========================================================
            // FIND USER
            // ========================================================

            var user =
                await _userManager.FindByEmailAsync(
                    email);


            if (user == null)
            {
                AddInvalidLoginError();

                return Page();
            }


            // ========================================================
            // ACCOUNT STATUS
            // ========================================================

            if (!await _userManager.IsEmailConfirmedAsync(user)
                &&
                _userManager.Options.SignIn.RequireConfirmedEmail)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Please confirm your email address before signing in.");

                return Page();
            }


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


            // ========================================================
            // EMPLOYEE-ONLY ACCOUNT
            //
            // Only an account that is:
            //
            // Employee
            // AND NOT Admin
            // AND NOT SuperAdmin
            //
            // is restricted to one active device.
            // ========================================================

            var isEmployeeOnly =
                isEmployee &&
                !isAdmin &&
                !isSuperAdmin;


            string? deviceId = null;


            // ========================================================
            // VERIFY PASSWORD FIRST
            //
            // IMPORTANT:
            //
            // We NEVER create the device lock before the password
            // has been successfully verified.
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);


            if (passwordResult.IsLockedOut)
            {
                return RedirectToPage(
                    "./Lockout");
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
                "LOGIN PASSWORD VERIFIED. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}",
                user.Id,
                user.Email,
                isEmployeeOnly);


            // ========================================================
            // EMPLOYEE SINGLE-SESSION LOCK
            // ========================================================

            if (isEmployeeOnly)
            {
                deviceId =
                    Guid.NewGuid().ToString("N");


                _logger.LogInformation(
                    "EMPLOYEE LOGIN LOCK ATTEMPT. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                EmployeeLockResult lockResult;

                try
                {
                    lockResult =
                        await TryAcquireEmployeeLockAsync(
                            user.Id,
                            deviceId);
                }
                catch (Exception ex)
                {
                    // ------------------------------------------------
                    // DATABASE FAILURE
                    //
                    // Do NOT tell the employee that another device
                    // is logged in when the actual problem is the
                    // database.
                    // ------------------------------------------------

                    _logger.LogError(
                        ex,
                        "EMPLOYEE LOGIN LOCK DATABASE ERROR. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "We could not verify your active login session. Please try again.");

                    return Page();
                }


                // ----------------------------------------------------
                // ANOTHER DEVICE ALREADY OWNS THE LOCK
                // ----------------------------------------------------

                if (lockResult ==
                    EmployeeLockResult.AlreadyActive)
                {
                    _logger.LogWarning(
                        "EMPLOYEE LOGIN BLOCKED. ANOTHER ACTIVE DEVICE EXISTS. UserId={UserId}",
                        user.Id);


                    ModelState.AddModelError(
                        string.Empty,
                        AlreadyLoggedInMessage);


                    return Page();
                }


                // ----------------------------------------------------
                // LOCK SUCCESSFULLY CREATED
                // ----------------------------------------------------

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK ACQUIRED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }


            // ========================================================
            // SIGN IN
            // ========================================================

            try
            {
                // ----------------------------------------------------
                // EMPLOYEE
                // ----------------------------------------------------

                if (isEmployeeOnly &&
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

                // ----------------------------------------------------
                // ADMIN / SUPERADMIN
                // ----------------------------------------------------

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


                // ----------------------------------------------------
                // CRITICAL CLEANUP
                //
                // If the database lock was acquired but Identity
                // authentication failed, release the lock.
                // ----------------------------------------------------

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

                        SameSite =
                            SameSiteMode.Lax,

                        IsEssential = true,

                        MaxAge =
                            TimeSpan.FromDays(365),

                        Path = "/"
                    });
            }


            // ========================================================
            // SUCCESS LOG
            // ========================================================

            _logger.LogInformation(
                "LOGIN SUCCESS. UserId={UserId}, Email={Email}, EmployeeOnly={EmployeeOnly}, DeviceId={DeviceId}",
                user.Id,
                user.Email,
                isEmployeeOnly,
                deviceId);


            // ========================================================
            // EMPLOYEE
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect(
                    "/employee-home");
            }


            // ========================================================
            // ADMIN / SUPERADMIN
            // ========================================================

            if (!string.IsNullOrWhiteSpace(
                    ReturnUrl) &&
                ReturnUrl != "/" &&
                Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(
                    ReturnUrl);
            }


            return LocalRedirect("/");
        }


        // ============================================================
        // EMPLOYEE LOCK RESULT
        // ============================================================

        private enum EmployeeLockResult
        {
            Acquired,

            AlreadyActive
        }


        // ============================================================
        // ATOMIC EMPLOYEE LOCK
        // ============================================================
        //
        // PostgreSQL is the authority.
        //
        // The database MUST have:
        //
        // UNIQUE("UserId")
        //
        // We deliberately do NOT:
        //
        // SELECT -> check -> INSERT
        //
        // because that has a race condition.
        //
        // Instead:
        //
        // INSERT ... ON CONFLICT DO NOTHING
        //
        // means two simultaneous requests cannot both win.
        // ============================================================

        private async Task<EmployeeLockResult>
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


            if (postgres.State !=
                ConnectionState.Open)
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


            // ========================================================
            // NULL
            //
            // PostgreSQL found an existing UserId.
            // ========================================================

            if (result == null ||
                result == DBNull.Value)
            {
                _logger.LogWarning(
                    "EMPLOYEE LOCK DENIED. UserId={UserId} already has an active device.",
                    userId);

                return EmployeeLockResult.AlreadyActive;
            }


            // ========================================================
            // LOCK CREATED
            // ========================================================

            _logger.LogInformation(
                "EMPLOYEE LOCK CREATED. UserId={UserId}, DeviceId={DeviceId}",
                userId,
                deviceId);


            return EmployeeLockResult.Acquired;
        }


        // ============================================================
        // REMOVE EMPLOYEE LOCK
        // ============================================================
        //
        // Both UserId AND DeviceId are required.
        //
        // This is important.
        //
        // An old browser/circuit must NEVER be able to delete a
        // newer device's lock.
        // ============================================================

        private async Task RemoveEmployeeLockAsync(
            string userId,
            string deviceId)
        {
            if (string.IsNullOrWhiteSpace(
                    userId) ||
                string.IsNullOrWhiteSpace(
                    deviceId))
            {
                return;
            }


            try
            {
                await using var db =
                    await _dbFactory.CreateDbContextAsync();


                var connection =
                    db.Database.GetDbConnection();


                if (connection is not NpgsqlConnection postgres)
                {
                    return;
                }


                if (postgres.State !=
                    ConnectionState.Open)
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


                var deleted =
                    await command.ExecuteNonQueryAsync();


                _logger.LogInformation(
                    "EMPLOYEE LOCK CLEANUP. UserId={UserId}, DeviceId={DeviceId}, Deleted={Deleted}",
                    userId,
                    deviceId,
                    deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE LOCK CLEANUP FAILED. UserId={UserId}, DeviceId={DeviceId}",
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