using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Shared.Data;

namespace Payroll.Web.Services
{
    /// <summary>
    /// Enforces one active login session per Employee account.
    ///
    /// Admin and SuperAdmin accounts are not restricted.
    ///
    /// The database unique index on EmployeeDeviceLock.UserId
    /// provides the final concurrency protection.
    /// </summary>
    public sealed class EmployeeSingleSessionSignInManager
        : SignInManager<IdentityUser>
    {
        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        private readonly IHttpContextAccessor
            _httpContextAccessor;

        private readonly ILogger<EmployeeSingleSessionSignInManager>
            _sessionLogger;


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
            _dbFactory = dbFactory;
            _httpContextAccessor = contextAccessor;
            _sessionLogger = sessionLogger;
        }


        // ============================================================
        // PASSWORD LOGIN
        // ============================================================

        public override async Task<SignInResult> PasswordSignInAsync(
            IdentityUser user,
            string password,
            bool isPersistent,
            bool lockoutOnFailure)
        {
            ArgumentNullException.ThrowIfNull(user);

            // --------------------------------------------------------
            // 1. VERIFY PASSWORD FIRST
            // --------------------------------------------------------

            var passwordResult =
                await CheckPasswordSignInAsync(
                    user,
                    password,
                    lockoutOnFailure);

            if (!passwordResult.Succeeded)
            {
                return passwordResult;
            }


            // --------------------------------------------------------
            // 2. DETERMINE ACCOUNT TYPE
            // --------------------------------------------------------

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


            // --------------------------------------------------------
            // 3. ADMIN / SUPERADMIN
            //
            // These accounts are not subject to employee
            // single-device locking.
            // --------------------------------------------------------

            if (!isEmployeeOnly)
            {
                return await SignInOrTwoFactorAsync(
                    user,
                    isPersistent);
            }


            // --------------------------------------------------------
            // 4. CREATE UNIQUE DEVICE SESSION ID
            // --------------------------------------------------------

            var deviceId =
                Guid.NewGuid().ToString("N");


            _sessionLogger.LogInformation(
                "EMPLOYEE LOGIN ATTEMPT. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);


            // --------------------------------------------------------
            // 5. ATOMIC DATABASE LOCK
            //
            // The database has:
            //
            // UNIQUE(UserId)
            //
            // Therefore only ONE request can acquire the lock.
            // --------------------------------------------------------

            var lockAcquired =
                await TryAcquireEmployeeLockAsync(
                    user.Id,
                    deviceId);


            if (!lockAcquired)
            {
                _sessionLogger.LogWarning(
                    "EMPLOYEE LOGIN BLOCKED. Existing active session. UserId={UserId}",
                    user.Id);

                // The built-in Identity UI will display its normal
                // failed-login message.
                return SignInResult.Failed;
            }


            // --------------------------------------------------------
            // 6. CREATE AUTHENTICATION COOKIE
            // --------------------------------------------------------

            try
            {
                var signInResult =
                    await SignInOrTwoFactorAsync(
                        user,
                        isPersistent);


                // ----------------------------------------------------
                // If Identity did not complete authentication,
                // release the database lock immediately.
                // ----------------------------------------------------

                if (!signInResult.Succeeded)
                {
                    await RemoveEmployeeLockAsync(
                        user.Id,
                        deviceId);

                    return signInResult;
                }


                // ----------------------------------------------------
                // 7. STORE DEVICE OWNERSHIP COOKIE
                // ----------------------------------------------------

                var httpContext =
                    _httpContextAccessor.HttpContext;

                if (httpContext == null)
                {
                    _sessionLogger.LogError(
                        "HTTP context was unavailable after employee login. UserId={UserId}",
                        user.Id);

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
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true,

                        // Long enough to survive the normal browser
                        // session. Logout explicitly deletes it.
                        MaxAge = TimeSpan.FromDays(365),

                        Path = "/"
                    });


                _sessionLogger.LogInformation(
                    "EMPLOYEE LOGIN SUCCESS. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                return signInResult;
            }
            catch (Exception ex)
            {
                _sessionLogger.LogError(
                    ex,
                    "EMPLOYEE LOGIN FAILED AFTER LOCK ACQUISITION. UserId={UserId}",
                    user.Id);

                // Never leave a database lock behind when the
                // authentication process itself fails.
                await RemoveEmployeeLockAsync(
                    user.Id,
                    deviceId);

                throw;
            }
        }


        // ============================================================
        // CREATE EMPLOYEE DEVICE LOCK
        // ============================================================

        private async Task<bool> TryAcquireEmployeeLockAsync(
            string userId,
            string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();


            var now =
                DateTime.UtcNow;


            var lockRecord =
                new EmployeeDeviceLock
                {
                    Id = Guid.NewGuid(),

                    UserId = userId,

                    DeviceId = deviceId,

                    CreatedAtUtc = now,

                    LastSeenAtUtc = now
                };


            db.EmployeeDeviceLocks.Add(
                lockRecord);


            try
            {
                await db.SaveChangesAsync();

                return true;
            }
            catch (DbUpdateException ex)
            {
                // ====================================================
                // IMPORTANT
                //
                // PostgreSQL unique constraint:
                //
                // UNIQUE(UserId)
                //
                // If another device already owns the employee lock,
                // this INSERT fails atomically.
                // ====================================================

                _sessionLogger.LogWarning(
                    ex,
                    "EMPLOYEE DEVICE LOCK ALREADY EXISTS. UserId={UserId}",
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


                _sessionLogger.LogInformation(
                    "EMPLOYEE DEVICE LOCK REMOVED. UserId={UserId}",
                    userId);
            }
            catch (Exception ex)
            {
                _sessionLogger.LogError(
                    ex,
                    "FAILED TO REMOVE EMPLOYEE DEVICE LOCK. UserId={UserId}",
                    userId);
            }
        }
    }
}