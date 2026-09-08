using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Payroll.Shared.Data;
using System.Data;
using System.Security.Claims;

namespace Payroll.Web.Services
{
    /// <summary>
    /// Enforces one active login session for Employee-only accounts.
    ///
    /// Employee-only:
    ///     Employee = true
    ///     Admin = false
    ///     SuperAdmin = false
    ///
    /// Admin and SuperAdmin accounts are NOT restricted.
    ///
    /// The PostgreSQL UNIQUE(UserId) constraint is the final
    /// concurrency authority.
    /// </summary>
    public sealed class EmployeeSingleSessionSignInManager
        : SignInManager<IdentityUser>
    {
        // ============================================================
        // CONSTANTS
        // ============================================================

        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        public const string DeviceClaimType =
            "BioMetric-Employee-Device";

        public const string AlreadyLoggedInKey =
            "BioMetric.EmployeeAlreadyLoggedIn";


        // ============================================================
        // SERVICES
        // ============================================================

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        private readonly IHttpContextAccessor
            _httpContextAccessor;

        private readonly ILogger<EmployeeSingleSessionSignInManager>
            _logger;


        // ============================================================
        // CONSTRUCTOR
        // ============================================================

        public EmployeeSingleSessionSignInManager(
            UserManager<IdentityUser> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<IdentityUser> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<IdentityUser>> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<IdentityUser> confirmation,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<EmployeeSingleSessionSignInManager> sessionLogger)
            : base(
                userManager,
                contextAccessor,
                claimsFactory,
                optionsAccessor,
                logger,
                schemes,
                confirmation)
        {
            _dbFactory =
                dbFactory;

            _httpContextAccessor =
                contextAccessor;

            _logger =
                sessionLogger;
        }


        // ============================================================
        // PASSWORD LOGIN
        // ============================================================

        public override async Task<SignInResult>
            PasswordSignInAsync(
                IdentityUser user,
                string password,
                bool isPersistent,
                bool lockoutOnFailure)
        {
            ArgumentNullException.ThrowIfNull(user);


            // ========================================================
            // 1. VERIFY PASSWORD
            // ========================================================

            var passwordResult =
                await CheckPasswordSignInAsync(
                    user,
                    password,
                    lockoutOnFailure);


            if (!passwordResult.Succeeded)
            {
                return passwordResult;
            }


            // ========================================================
            // 2. CHECK ROLES
            // ========================================================

            var isEmployee =
                await UserManager.IsInRoleAsync(
                    user,
                    "Employee");

            var isAdmin =
                await UserManager.IsInRoleAsync(
                    user,
                    "Admin");

            var isSuperAdmin =
                await UserManager.IsInRoleAsync(
                    user,
                    "SuperAdmin");


            var isEmployeeOnly =
                isEmployee &&
                !isAdmin &&
                !isSuperAdmin;


            // ========================================================
            // 3. ADMIN / SUPERADMIN
            //
            // No single-session restriction.
            // ========================================================

            if (!isEmployeeOnly)
            {
                return await SignInOrTwoFactorAsync(
                    user,
                    isPersistent);
            }


            // ========================================================
            // 4. CHECK WHETHER THIS BROWSER ALREADY OWNS THE SESSION
            // ========================================================
            //
            // If the same browser/device already has the employee's
            // device cookie, don't create another lock.
            //
            // This allows normal tabs in the same browser.
            // ========================================================

            var existingDeviceId =
                GetCurrentDeviceId();


            if (!string.IsNullOrWhiteSpace(existingDeviceId))
            {
                var ownsCurrentLock =
                    await CurrentDeviceOwnsLockAsync(
                        user.Id,
                        existingDeviceId);


                if (ownsCurrentLock)
                {
                    _logger.LogInformation(
                        "EMPLOYEE SAME-SESSION LOGIN. UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        existingDeviceId);


                    return await SignInWithCurrentEmployeeSessionAsync(
                        user,
                        isPersistent,
                        existingDeviceId);
                }
            }


            // ========================================================
            // 5. NEW DEVICE
            // ========================================================

            var newDeviceId =
                Guid.NewGuid().ToString("N");


            _logger.LogInformation(
                "EMPLOYEE NEW-DEVICE LOGIN ATTEMPT. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                newDeviceId);


            // ========================================================
            // 6. TRY TO ACQUIRE LOCK
            // ========================================================

            var lockAcquired =
                await TryAcquireEmployeeLockAsync(
                    user.Id,
                    newDeviceId);


            if (!lockAcquired)
            {
                // ====================================================
                // ANOTHER DEVICE IS ALREADY LOGGED IN
                // ====================================================
                //
                // The password is CORRECT.
                //
                // Therefore this is not a password failure.
                //
                // We mark the HTTP request so the login flow can
                // distinguish the situation.
                // ====================================================

                var httpContext =
                    _httpContextAccessor.HttpContext;


                if (httpContext != null)
                {
                    httpContext.Items[
                        AlreadyLoggedInKey] = true;
                }


                _logger.LogWarning(
                    "EMPLOYEE LOGIN BLOCKED. EXISTING SESSION EXISTS. UserId={UserId}",
                    user.Id);


                /*
                 * IMPORTANT:
                 *
                 * Because this project uses the built-in Identity UI
                 * and does not have a custom Login.cshtml, there is
                 * no custom button available here.
                 *
                 * We therefore invalidate the previous session and
                 * require the employee to submit the login once more.
                 *
                 * This is deliberately NOT done until the password
                 * has already been verified above.
                 */

                var invalidated =
                    await InvalidateExistingEmployeeSessionAsync(
                        user.Id);


                if (!invalidated)
                {
                    _logger.LogError(
                        "EMPLOYEE EXISTING SESSION COULD NOT BE INVALIDATED. UserId={UserId}",
                        user.Id);

                    return SignInResult.Failed;
                }


                // ----------------------------------------------------
                // Create the new lock after invalidating old session.
                // ----------------------------------------------------

                var replacementDeviceId =
                    Guid.NewGuid().ToString("N");


                var replacementAcquired =
                    await TryAcquireEmployeeLockAsync(
                        user.Id,
                        replacementDeviceId);


                if (!replacementAcquired)
                {
                    _logger.LogError(
                        "EMPLOYEE NEW SESSION LOCK COULD NOT BE CREATED AFTER INVALIDATION. UserId={UserId}",
                        user.Id);

                    return SignInResult.Failed;
                }


                // ----------------------------------------------------
                // Save the replacement device ID for this request.
                // ----------------------------------------------------

                return await AuthenticateEmployeeAsync(
                    user,
                    isPersistent,
                    replacementDeviceId);
            }


            // ========================================================
            // 7. FIRST LOGIN ON THIS ACCOUNT
            // ========================================================

            return await AuthenticateEmployeeAsync(
                user,
                isPersistent,
                newDeviceId);
        }


        // ============================================================
        // GET CURRENT DEVICE ID
        // ============================================================

        private string? GetCurrentDeviceId()
        {
            var httpContext =
                _httpContextAccessor.HttpContext;


            if (httpContext == null)
            {
                return null;
            }


            // Claim first.
            var claim =
                httpContext.User.FindFirstValue(
                    DeviceClaimType);


            if (!string.IsNullOrWhiteSpace(claim))
            {
                return claim.Trim();
            }


            // Cookie fallback.
            if (
                httpContext.Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var cookieValue) &&
                !string.IsNullOrWhiteSpace(cookieValue))
            {
                return cookieValue.Trim();
            }


            return null;
        }


        // ============================================================
        // CHECK CURRENT DEVICE OWNERSHIP
        // ============================================================

        private async Task<bool>
            CurrentDeviceOwnsLockAsync(
                string userId,
                string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();


            return await db.EmployeeDeviceLocks
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserId == userId &&
                        x.DeviceId == deviceId);
        }


        // ============================================================
        // AUTHENTICATE EMPLOYEE
        // ============================================================

        private async Task<SignInResult>
            AuthenticateEmployeeAsync(
                IdentityUser user,
                bool isPersistent,
                string deviceId)
        {
            try
            {
                var claims =
                    new[]
                    {
                        new Claim(
                            DeviceClaimType,
                            deviceId)
                    };


                await SignInWithClaimsAsync(
                    user,
                    isPersistent,
                    claims);


                var httpContext =
                    _httpContextAccessor.HttpContext;


                if (httpContext == null)
                {
                    await RemoveEmployeeLockAsync(
                        user.Id,
                        deviceId);

                    await base.SignOutAsync();

                    return SignInResult.Failed;
                }


                httpContext.Response.Cookies.Append(
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


                _logger.LogInformation(
                    "EMPLOYEE LOGIN SUCCESS. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                return SignInResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE AUTHENTICATION FAILED. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                await RemoveEmployeeLockAsync(
                    user.Id,
                    deviceId);


                throw;
            }
        }


        // ============================================================
        // SAME SESSION AUTHENTICATION
        // ============================================================

        private async Task<SignInResult>
            SignInWithCurrentEmployeeSessionAsync(
                IdentityUser user,
                bool isPersistent,
                string deviceId)
        {
            return await AuthenticateEmployeeAsync(
                user,
                isPersistent,
                deviceId);
        }


        // ============================================================
        // ACQUIRE LOCK
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


            var now =
                DateTime.UtcNow;


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
                now);

            command.Parameters.AddWithValue(
                "lastSeenAtUtc",
                now);


            var result =
                await command.ExecuteScalarAsync();


            return
                result != null &&
                result != DBNull.Value;
        }


        // ============================================================
        // INVALIDATE OLD EMPLOYEE SESSION
        // ============================================================
        //
        // SecurityStamp invalidation makes the old Identity cookie
        // invalid.
        //
        // The lock itself is also removed.
        // ============================================================

        private async Task<bool>
            InvalidateExistingEmployeeSessionAsync(
                string userId)
        {
            try
            {
                var existingUser =
                    await UserManager.FindByIdAsync(
                        userId);


                if (existingUser == null)
                {
                    return false;
                }


                // ----------------------------------------------------
                // Change Identity security stamp.
                // ----------------------------------------------------

                var stampResult =
                    await UserManager.UpdateSecurityStampAsync(
                        existingUser);


                if (!stampResult.Succeeded)
                {
                    foreach (var error in stampResult.Errors)
                    {
                        _logger.LogError(
                            "SECURITY STAMP ERROR. UserId={UserId}, Code={Code}, Description={Description}",
                            userId,
                            error.Code,
                            error.Description);
                    }


                    return false;
                }


                // ----------------------------------------------------
                // Remove old device lock.
                // ----------------------------------------------------

                await using var db =
                    await _dbFactory.CreateDbContextAsync();


                var oldLock =
                    await db.EmployeeDeviceLocks
                        .FirstOrDefaultAsync(
                            x => x.UserId == userId);


                if (oldLock != null)
                {
                    db.EmployeeDeviceLocks.Remove(
                        oldLock);

                    await db.SaveChangesAsync();
                }


                _logger.LogWarning(
                    "EXISTING EMPLOYEE SESSION INVALIDATED. UserId={UserId}",
                    userId);


                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "FAILED TO INVALIDATE EXISTING EMPLOYEE SESSION. UserId={UserId}",
                    userId);

                return false;
            }
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
                    "EMPLOYEE DEVICE LOCK REMOVED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE DEVICE LOCK REMOVAL FAILED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
        }
    }
}