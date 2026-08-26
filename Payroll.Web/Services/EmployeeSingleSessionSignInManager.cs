using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Payroll.Shared.Data;

namespace Payroll.Web.Services
{
    /// <summary>
    /// Enforces one active login session per Employee-only account.
    ///
    /// Employee-only:
    ///     Employee = true
    ///     Admin = false
    ///     SuperAdmin = false
    ///
    /// Admin and SuperAdmin accounts are allowed to have multiple
    /// simultaneous login sessions.
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
            _dbFactory =
                dbFactory;

            _httpContextAccessor =
                contextAccessor;

            _sessionLogger =
                sessionLogger;
        }


        // ============================================================
        // PASSWORD SIGN IN
        // ============================================================

        public override async Task<SignInResult>
            PasswordSignInAsync(
                IdentityUser user,
                string password,
                bool isPersistent,
                bool lockoutOnFailure)
        {
            ArgumentNullException.ThrowIfNull(
                user);


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
            // No single-device restriction.
            // ========================================================

            if (!isEmployeeOnly)
            {
                return await SignInOrTwoFactorAsync(
                    user,
                    isPersistent);
            }


            // ========================================================
            // 4. GENERATE DEVICE ID
            // ========================================================

            var deviceId =
                Guid.NewGuid().ToString("N");


            _sessionLogger.LogInformation(
                "EMPLOYEE SINGLE-SESSION LOGIN ATTEMPT. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);


            // ========================================================
            // 5. ACQUIRE DATABASE LOCK
            // ========================================================

            var lockAcquired =
                await TryAcquireEmployeeLockAsync(
                    user.Id,
                    deviceId);


            if (!lockAcquired)
            {
                _sessionLogger.LogWarning(
                    "EMPLOYEE SINGLE-SESSION LOGIN BLOCKED. UserId={UserId}",
                    user.Id);


                // ----------------------------------------------------
                // Tell the Login Page why the operation failed.
                // ----------------------------------------------------

                var httpContext =
                    _httpContextAccessor.HttpContext;


                if (httpContext != null)
                {
                    httpContext.Items[
                        AlreadyLoggedInKey] = true;
                }


                return SignInResult.Failed;
            }


            // ========================================================
            // 6. AUTHENTICATE
            // ========================================================

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


                // ====================================================
                // 7. DEVICE COOKIE
                // ====================================================

                var httpContext =
                    _httpContextAccessor.HttpContext;


                if (httpContext == null)
                {
                    _sessionLogger.LogError(
                        "HTTP context unavailable after employee authentication. UserId={UserId}",
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

                        SameSite =
                            SameSiteMode.Lax,

                        IsEssential = true,

                        MaxAge =
                            TimeSpan.FromDays(365),

                        Path = "/"
                    });


                _sessionLogger.LogInformation(
                    "EMPLOYEE SINGLE-SESSION LOGIN SUCCESS. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                return SignInResult.Success;
            }
            catch (Exception ex)
            {
                _sessionLogger.LogError(
                    ex,
                    "EMPLOYEE AUTHENTICATION FAILED AFTER LOCK. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);


                // ----------------------------------------------------
                // Never leave a lock behind when authentication fails.
                // ----------------------------------------------------

                await RemoveEmployeeLockAsync(
                    user.Id,
                    deviceId);


                throw;
            }
        }


        // ============================================================
        // CREATE EMPLOYEE LOCK
        // ============================================================
        //
        // EF INSERT is protected by the PostgreSQL UNIQUE(UserId)
        // constraint.
        //
        // Two simultaneous requests:
        //
        // Request A -> INSERT succeeds
        // Request B -> unique constraint fails
        //
        // Therefore only one request can acquire the lock.
        // ============================================================

        private async Task<bool>
            TryAcquireEmployeeLockAsync(
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
                    Id =
                        Guid.NewGuid(),

                    UserId =
                        userId,

                    DeviceId =
                        deviceId,

                    CreatedAtUtc =
                        now,

                    LastSeenAtUtc =
                        now
                };


            db.EmployeeDeviceLocks.Add(
                lockRecord);


            try
            {
                await db.SaveChangesAsync();


                _sessionLogger.LogInformation(
                    "EMPLOYEE DEVICE LOCK CREATED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);


                return true;
            }
            catch (DbUpdateException ex)
            {
                // ====================================================
                // IMPORTANT
                //
                // We intentionally treat the database unique
                // constraint as the concurrency authority.
                // ====================================================

                _sessionLogger.LogWarning(
                    ex,
                    "EMPLOYEE DEVICE LOCK INSERT FAILED. UserId={UserId}",
                    userId);


                return false;
            }
        }


        // ============================================================
        // REMOVE EMPLOYEE LOCK
        // ============================================================
        //
        // MUST match BOTH:
        //
        // UserId
        // DeviceId
        //
        // This prevents an old session from deleting a new session's
        // lock.
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


                _sessionLogger.LogInformation(
                    "EMPLOYEE DEVICE LOCK REMOVED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
            catch (Exception ex)
            {
                _sessionLogger.LogError(
                    ex,
                    "EMPLOYEE DEVICE LOCK REMOVAL FAILED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);
            }
        }
    }
}