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

        // ============================================================
        // GET
        // ============================================================

        public IActionResult OnGet()
        {
            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
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
                // ----------------------------------------------------
                // RELEASE EMPLOYEE DEVICE LOCK
                // ----------------------------------------------------

                if (user != null)
                {
                    await ReleaseEmployeeDeviceAsync(user);
                }

                // ----------------------------------------------------
                // SIGN OUT ASP.NET CORE IDENTITY
                // ----------------------------------------------------

                await _signInManager.SignOutAsync();

                // ----------------------------------------------------
                // DELETE DEVICE COOKIE
                // ----------------------------------------------------

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
                    "User logged out successfully. UserId={UserId}",
                    user?.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Logout cleanup failed. UserId={UserId}",
                    user?.Id);

                // ----------------------------------------------------
                // ALWAYS SIGN OUT
                // ----------------------------------------------------

                try
                {
                    await _signInManager.SignOutAsync();
                }
                catch (Exception signOutException)
                {
                    _logger.LogError(
                        signOutException,
                        "Identity sign-out failed. UserId={UserId}",
                        user?.Id);
                }

                // ----------------------------------------------------
                // ALWAYS DELETE DEVICE COOKIE
                // ----------------------------------------------------

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
            // SAFE RETURN URL
            // ========================================================

            if (
                !string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // ========================================================
            // DEFAULT LOGIN PAGE
            // ========================================================

            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
        }

        // ============================================================
        // RELEASE EMPLOYEE DEVICE LOCK
        // ============================================================

        private async Task ReleaseEmployeeDeviceAsync(
            IdentityUser user)
        {
            // --------------------------------------------------------
            // CHECK ROLES
            // --------------------------------------------------------

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
            // ONLY EMPLOYEE-ONLY ACCOUNTS USE DEVICE LOCKING
            // --------------------------------------------------------

            if (
                !isEmployee ||
                isAdmin ||
                isSuperAdmin)
            {
                return;
            }

            // --------------------------------------------------------
            // GET DEVICE COOKIE
            // --------------------------------------------------------

            if (
                !Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning(
                    "Employee logout had no device cookie. UserId={UserId}",
                    user.Id);

                return;
            }

            deviceId = deviceId.Trim();

            // --------------------------------------------------------
            // DATABASE
            // --------------------------------------------------------

            await using var db =
                await _dbFactory.CreateDbContextAsync();

            // --------------------------------------------------------
            // FIND ACTIVE DEVICE TOKEN
            // --------------------------------------------------------

            var token =
                await db
                    .Set<IdentityUserToken<string>>()
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.LoginProvider ==
                                LoginProvider &&
                            x.Name ==
                                TokenName);

            // No active device lock.
            if (token == null)
            {
                _logger.LogInformation(
                    "No active employee device lock found during logout. UserId={UserId}",
                    user.Id);

                return;
            }

            // --------------------------------------------------------
            // ONLY OWNER DEVICE CAN RELEASE LOCK
            // --------------------------------------------------------

            if (
                !string.Equals(
                    token.Value?.Trim(),
                    deviceId,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Employee logout device ID did not match active device. UserId={UserId}",
                    user.Id);

                return;
            }

            // --------------------------------------------------------
            // REMOVE ACTIVE DEVICE
            // --------------------------------------------------------

            db.Remove(token);

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Employee device lock released successfully. UserId={UserId}",
                user.Id);
        }
    }
}