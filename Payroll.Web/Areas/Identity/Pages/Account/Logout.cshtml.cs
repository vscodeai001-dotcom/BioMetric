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
        // POST
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
                    await ReleaseEmployeeDeviceAsync(user);
                }


                // ====================================================
                // IDENTITY SIGN OUT
                // ====================================================

                await _signInManager.SignOutAsync();


                // ====================================================
                // DELETE DEVICE COOKIE
                // ====================================================

                Response.Cookies.Delete(
                    DeviceCookieName,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });


                _logger.LogInformation(
                    "LOGOUT SUCCESS. UserId={UserId}",
                    user?.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "LOGOUT CLEANUP ERROR. UserId={UserId}",
                    user?.Id);


                try
                {
                    await _signInManager.SignOutAsync();
                }
                catch
                {
                }


                Response.Cookies.Delete(
                    DeviceCookieName,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });
            }


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
        // RELEASE DEVICE LOCK
        // ============================================================

        private async Task
            ReleaseEmployeeDeviceAsync(
                IdentityUser user)
        {
            // ========================================================
            // ROLE CHECK
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


            // Only employee-only accounts use device locking.
            if (
                !isEmployee ||
                isAdmin ||
                isSuperAdmin)
            {
                return;
            }


            // ========================================================
            // DEVICE COOKIE
            // ========================================================

            if (
                !Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "LOGOUT: No employee device cookie. UserId={UserId}",
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
            // FIND LOCK
            // ========================================================

            var lockRecord =
                await db.EmployeeDeviceLocks
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id);


            if (lockRecord == null)
            {
                _logger.LogInformation(
                    "LOGOUT: No device lock found. UserId={UserId}",
                    user.Id);

                return;
            }


            // ========================================================
            // VERIFY DEVICE OWNER
            // ========================================================

            if (
                !string.Equals(
                    lockRecord.DeviceId,
                    deviceId,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "LOGOUT: Device ID mismatch. UserId={UserId}",
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
    }
}