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
                // ====================================================
                // RELEASE EMPLOYEE LOCK
                // ====================================================

                if (user != null)
                {
                    await ReleaseEmployeeDeviceLockAsync(user);
                }

                // ====================================================
                // SIGN OUT
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
                        IsEssential = true,
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

                // ====================================================
                // ALWAYS SIGN OUT
                // ====================================================

                try
                {
                    await _signInManager.SignOutAsync();
                }
                catch (Exception signOutException)
                {
                    _logger.LogError(
                        signOutException,
                        "IDENTITY SIGN-OUT FAILED. UserId={UserId}",
                        user?.Id);
                }

                // ====================================================
                // ALWAYS DELETE COOKIE
                // ====================================================

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

            // ========================================================
            // RETURN URL
            // ========================================================

            if (
                !string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // ========================================================
            // LOGIN PAGE
            // ========================================================

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
            // FIND LOCK BY USER
            //
            // IMPORTANT:
            // We do NOT require the browser cookie here.
            //
            // The authenticated Identity user is the authority.
            // ========================================================

            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var lockRecord =
                await db.EmployeeDeviceLocks
                    .FirstOrDefaultAsync(
                        x =>
                            x.UserId == user.Id);

            if (lockRecord == null)
            {
                _logger.LogInformation(
                    "LOGOUT: No employee device lock found. UserId={UserId}",
                    user.Id);

                return;
            }

            var deviceId =
                lockRecord.DeviceId;

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