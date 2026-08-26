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
                    await ReleaseEmployeeDeviceLockAsync(user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMPLOYEE DEVICE LOCK RELEASE FAILED DURING LOGOUT. UserId={UserId}",
                    user?.Id);
            }


            // ========================================================
            // SIGN OUT IDENTITY
            // ========================================================

            try
            {
                await _signInManager.SignOutAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IDENTITY SIGN OUT FAILED. UserId={UserId}",
                    user?.Id);
            }


            // ========================================================
            // DELETE DEVICE COOKIE
            // ========================================================

            DeleteDeviceCookie();


            _logger.LogInformation(
                "LOGOUT COMPLETED. UserId={UserId}",
                user?.Id);


            // ========================================================
            // RETURN URL
            // ========================================================

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
        // RELEASE EMPLOYEE DEVICE LOCK
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


            // --------------------------------------------------------
            // Only employee-only accounts use the device lock.
            // --------------------------------------------------------

            if (
                !isEmployee ||
                isAdmin ||
                isSuperAdmin)
            {
                return;
            }


            // ========================================================
            // READ DEVICE COOKIE
            // ========================================================

            if (
                !Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "LOGOUT: Device cookie missing. UserId={UserId}",
                    user.Id);

                return;
            }


            deviceId =
                deviceId.Trim();


            // ========================================================
            // DATABASE
            // ========================================================

            await using var db =
                await _dbFactory.CreateDbContextAsync();


            // ========================================================
            // MATCH BOTH USER AND DEVICE
            //
            // IMPORTANT:
            //
            // We do not delete a lock belonging to another device.
            // ========================================================

            var lockRecord =
                await db.EmployeeDeviceLocks
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.DeviceId == deviceId);


            if (lockRecord == null)
            {
                _logger.LogInformation(
                    "LOGOUT: No matching device lock. UserId={UserId}",
                    user.Id);

                return;
            }


            // ========================================================
            // DELETE LOCK
            // ========================================================

            db.EmployeeDeviceLocks.Remove(
                lockRecord);


            await db.SaveChangesAsync();


            _logger.LogInformation(
                "EMPLOYEE DEVICE LOCK RELEASED. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);
        }


        // ============================================================
        // DELETE COOKIE
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