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

        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<LogoutModel> _logger;

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

        public IActionResult OnGet()
        {
            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
        }

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
                    "Employee device lock cleanup failed during logout. UserId={UserId}",
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
                    "Identity sign-out failed. UserId={UserId}",
                    user?.Id);
            }

            DeleteDeviceCookie();

            _logger.LogInformation(
                "LOGOUT COMPLETED. UserId={UserId}",
                user?.Id);

            if (!string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
        }

        private async Task ReleaseEmployeeDeviceLockAsync(
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

            if (!isEmployee ||
                isAdmin ||
                isSuperAdmin)
            {
                return;
            }

            if (!Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "LOGOUT: Device cookie missing. UserId={UserId}",
                    user.Id);

                return;
            }

            deviceId = deviceId.Trim();

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
                    "LOGOUT: No matching device lock. UserId={UserId}",
                    user.Id);

                return;
            }

            db.EmployeeDeviceLocks.Remove(lockRecord);

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "EMPLOYEE DEVICE LOCK RELEASED. UserId={UserId}, DeviceId={DeviceId}",
                user.Id,
                deviceId);
        }

        private void DeleteDeviceCookie()
        {
            Response.Cookies.Delete(
                DeviceCookieName,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Path = "/"
                });
        }
    }
}