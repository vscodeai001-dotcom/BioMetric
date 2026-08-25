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

        private const string LoginProvider =
            "BioMetric";

        private const string TokenName =
            "ActiveDevice";

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
            var user = await _userManager.GetUserAsync(User);

            try
            {
                if (user != null)
                {
                    await ReleaseEmployeeDeviceAsync(user);
                }

                await _signInManager.SignOutAsync();

                Response.Cookies.Delete(DeviceCookieName);

                _logger.LogInformation(
                    "User logged out successfully. UserId={UserId}",
                    user?.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while logging out UserId={UserId}",
                    user?.Id);

                // Even if device cleanup fails,
                // make sure the authentication cookie is removed.
                await _signInManager.SignOutAsync();

                Response.Cookies.Delete(DeviceCookieName);
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
        }

        private async Task ReleaseEmployeeDeviceAsync(
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

            // Only Employee accounts have device locking.
            if (!isEmployee || isAdmin || isSuperAdmin)
            {
                return;
            }

            if (!Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "Employee logout had no device cookie. UserId={UserId}",
                    user.Id);

                return;
            }

            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var token =
                await db.Set<IdentityUserToken<string>>()
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.LoginProvider == LoginProvider &&
                            x.Name == TokenName);

            if (token == null)
            {
                return;
            }

            // Only the device that owns the lock can release it.
            if (token.Value != deviceId)
            {
                _logger.LogWarning(
                    "Employee logout device ID did not match active device. UserId={UserId}",
                    user.Id);

                return;
            }

            db.Remove(token);

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Employee device lock released. UserId={UserId}",
                user.Id);
        }
    }
}