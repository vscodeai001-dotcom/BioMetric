using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using Payroll.Shared.Data;

namespace Payroll.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private const string DeviceClaimType =
            "BioMetric-Employee-Device";


        private readonly SignInManager<IdentityUser>
            _signInManager;

        private readonly UserManager<IdentityUser>
            _userManager;

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        private readonly ILogger<LogoutModel>
            _logger;


        public LogoutModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _dbFactory = dbFactory;
            _logger = logger;
        }


        // ============================================================
        // GET
        // ============================================================

        public IActionResult OnGet()
        {
            return RedirectToPage(
                "/Account/Login",
                new
                {
                    area = "Identity"
                });
        }


        // ============================================================
        // POST LOGOUT
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            var user =
                await _userManager.GetUserAsync(User);


            try
            {
                if (user != null)
                {
                    await ReleaseEmployeeDeviceLockAsync(
                        user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE DEVICE LOCK CLEANUP FAILED DURING LOGOUT. UserId={UserId}",
                    user?.Id);
            }


            try
            {
                await _signInManager.SignOutAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IDENTITY SIGN-OUT FAILED. UserId={UserId}",
                    user?.Id);
            }


            DeleteDeviceCookie();


            _logger.LogInformation(
                "LOGOUT COMPLETED. UserId={UserId}",
                user?.Id);


            if (
                !string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }


            return RedirectToPage(
                "/Account/Login",
                new
                {
                    area = "Identity"
                });
        }


        // ============================================================
        // RELEASE EMPLOYEE LOCK
        // ============================================================

        private async Task
            ReleaseEmployeeDeviceLockAsync(
                IdentityUser user)
        {
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


            // Admin / SuperAdmin are unrestricted.

            if (
                !isEmployee ||
                isAdmin ||
                isSuperAdmin)
            {
                return;
            }


            // ========================================================
            // CLAIM FIRST
            // ========================================================

            var deviceId =
                User.FindFirstValue(
                    DeviceClaimType);


            // ========================================================
            // COOKIE FALLBACK
            // ========================================================

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out deviceId);
            }


            if (string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "LOGOUT: Device identity unavailable. UserId={UserId}",
                    user.Id);

                return;
            }


            deviceId =
                deviceId.Trim();


            // ========================================================
            // REMOVE ONLY THIS DEVICE'S LOCK
            // ========================================================

            await using var db =
                await _dbFactory.CreateDbContextAsync();


            var lockRecord =
                await db.EmployeeDeviceLocks
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.DeviceId == deviceId);


            if (lockRecord == null)
            {
                _logger.LogInformation(
                    "LOGOUT: No matching device lock. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);

                return;
            }


            db.EmployeeDeviceLocks.Remove(
                lockRecord);


            await db.SaveChangesAsync();


            _logger.LogInformation(
                "EMPLOYEE DEVICE LOCK RELEASED. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);
        }


        // ============================================================
        // DELETE DEVICE COOKIE
        // ============================================================

        private void DeleteDeviceCookie()
        {
            Response.Cookies.Delete(
                DeviceCookieName,
                new CookieOptions
                {
                    HttpOnly = true,

                    Secure = true,

                    SameSite =
                        SameSiteMode.Lax,

                    IsEssential = true,

                    Path = "/"
                });
        }
    }
}