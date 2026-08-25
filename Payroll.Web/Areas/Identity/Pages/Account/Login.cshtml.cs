using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Payroll.Shared.Data;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace Payroll.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private const string DeviceCookieName =
            "BioMetric-Employee-Device";

        private readonly SignInManager<IdentityUser>
            _signInManager;

        private readonly UserManager<IdentityUser>
            _userManager;

        private readonly IDbContextFactory<AppDbContext>
            _dbFactory;

        private readonly ILogger<LoginModel>
            _logger;


        public LoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _dbFactory = dbFactory;
            _logger = logger;
        }


        // ============================================================
        // FORM
        // ============================================================

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = "/";

        [TempData]
        public string? ErrorMessage { get; set; }


        public class InputModel
        {
            [Required(ErrorMessage = "Email is required.")]
            [EmailAddress(ErrorMessage = "Enter a valid email address.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }


        // ============================================================
        // GET
        // ============================================================

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : "/";

            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                ModelState.AddModelError(
                    string.Empty,
                    ErrorMessage);
            }
        }


        // ============================================================
        // POST
        // ============================================================

        public async Task<IActionResult> OnPostAsync(
            string? returnUrl = null)
        {
            ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : "/";


            // ========================================================
            // VALIDATE
            // ========================================================

            if (!ModelState.IsValid)
            {
                return Page();
            }


            var email = Input.Email.Trim();


            // ========================================================
            // FIND USER
            // ========================================================

            var user =
                await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

                return Page();
            }


            // ========================================================
            // ROLES
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

            var isEmployeeOnly =
                isEmployee &&
                !isAdmin &&
                !isSuperAdmin;


            // ========================================================
            // PASSWORD
            // ========================================================

            var passwordResult =
                await _signInManager.CheckPasswordSignInAsync(
                    user,
                    Input.Password,
                    lockoutOnFailure: false);


            if (passwordResult.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }


            if (passwordResult.IsNotAllowed)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This account is currently not allowed to sign in.");

                return Page();
            }


            if (!passwordResult.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Invalid email or password.");

                return Page();
            }


            _logger.LogInformation(
                "PASSWORD VERIFIED. UserId={UserId}",
                user.Id);


            // ========================================================
            // EMPLOYEE SINGLE DEVICE
            // ========================================================

            string? deviceId = null;


            if (isEmployeeOnly)
            {
                deviceId =
                    GetExistingDeviceId();

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId =
                        Guid.NewGuid().ToString("N");

                    _logger.LogInformation(
                        "NEW DEVICE GENERATED. UserId={UserId}, DeviceId={DeviceId}",
                        user.Id,
                        deviceId);
                }


                // ====================================================
                // ACQUIRE LOCK
                // ====================================================

                var acquired =
                    await AcquireEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);


                if (!acquired)
                {
                    _logger.LogWarning(
                        "SECOND DEVICE LOGIN BLOCKED. UserId={UserId}",
                        user.Id);

                    ModelState.AddModelError(
                        string.Empty,
                        "This employee account is already logged in on another device. Please log out from the other device before signing in here.");

                    return Page();
                }
            }


            // ========================================================
            // IDENTITY LOGIN
            // ========================================================

            try
            {
                await _signInManager.SignInAsync(
                    user,
                    isPersistent: Input.RememberMe);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Identity sign-in failed. UserId={UserId}",
                    user.Id);

                // Do not leave a device lock behind
                // if Identity authentication itself fails.

                if (isEmployeeOnly)
                {
                    await ReleaseEmployeeDeviceLockAsync(
                        user.Id,
                        deviceId);
                }

                ModelState.AddModelError(
                    string.Empty,
                    "Unable to sign in. Please try again.");

                return Page();
            }


            // ========================================================
            // DEVICE COOKIE
            // ========================================================

            if (isEmployeeOnly &&
                !string.IsNullOrWhiteSpace(deviceId))
            {
                SetDeviceCookie(deviceId);

                _logger.LogInformation(
                    "EMPLOYEE DEVICE LOCK ACTIVE. UserId={UserId}, DeviceId={DeviceId}",
                    user.Id,
                    deviceId);
            }


            // ========================================================
            // SUCCESS
            // ========================================================

            _logger.LogInformation(
                "LOGIN SUCCESS. UserId={UserId}, Email={Email}, Employee={Employee}, RememberMe={RememberMe}",
                user.Id,
                user.Email,
                isEmployeeOnly,
                Input.RememberMe);


            // ========================================================
            // EMPLOYEE
            // ========================================================

            if (isEmployeeOnly)
            {
                return LocalRedirect(
                    "/employee-home");
            }


            // ========================================================
            // ADMIN / SUPERADMIN
            // ========================================================

            if (
                !string.IsNullOrWhiteSpace(ReturnUrl) &&
                ReturnUrl != "/" &&
                Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return LocalRedirect("/");
        }


        // ============================================================
        // DEVICE COOKIE
        // ============================================================

        private string? GetExistingDeviceId()
        {
            if (
                Request.Cookies.TryGetValue(
                    DeviceCookieName,
                    out var deviceId) &&
                !string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId.Trim();
            }

            return null;
        }


        private void SetDeviceCookie(
            string deviceId)
        {
            Response.Cookies.Append(
                DeviceCookieName,
                deviceId,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/"
                });
        }


        // ============================================================
        // ACQUIRE EMPLOYEE DEVICE LOCK
        // ============================================================

        private async Task<bool>
            AcquireEmployeeDeviceLockAsync(
                string userId,
                string deviceId)
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);


            try
            {
                var connection =
                    db.Database.GetDbConnection();

                if (connection is not NpgsqlConnection
                    npgsqlConnection)
                {
                    await transaction.RollbackAsync();

                    _logger.LogError(
                        "DEVICE LOCK FAILED: PostgreSQL connection unavailable.");

                    return false;
                }


                if (npgsqlConnection.State !=
                    ConnectionState.Open)
                {
                    await npgsqlConnection.OpenAsync();
                }


                // ====================================================
                // LOCK USER ROW
                // ====================================================

                await using (
                    var command =
                        new NpgsqlCommand(
                            """
                            SELECT "Id"
                            FROM "AspNetUsers"
                            WHERE "Id" = @userId
                            FOR UPDATE
                            """,
                            npgsqlConnection,
                            (NpgsqlTransaction)
                                transaction.GetDbTransaction()))
                {
                    command.Parameters.AddWithValue(
                        "userId",
                        userId);

                    var result =
                        await command.ExecuteScalarAsync();

                    if (result == null)
                    {
                        await transaction.RollbackAsync();

                        return false;
                    }
                }


                // ====================================================
                // FIND EXISTING LOCK
                // ====================================================

                var existing =
                    await db.EmployeeDeviceLocks
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId);


                // ====================================================
                // EXISTING LOCK
                // ====================================================

                if (existing != null)
                {
                    existing.LastSeenAtUtc =
                        DateTime.UtcNow;


                    // SAME DEVICE
                    if (
                        string.Equals(
                            existing.DeviceId,
                            deviceId,
                            StringComparison.Ordinal))
                    {
                        await db.SaveChangesAsync();

                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "SAME DEVICE LOGIN ALLOWED. UserId={UserId}",
                            userId);

                        return true;
                    }


                    // DIFFERENT DEVICE
                    _logger.LogWarning(
                        "SECOND DEVICE REJECTED. UserId={UserId}, ExistingDevice={ExistingDevice}, IncomingDevice={IncomingDevice}",
                        userId,
                        existing.DeviceId,
                        deviceId);

                    await transaction.RollbackAsync();

                    return false;
                }


                // ====================================================
                // CREATE NEW LOCK
                // ====================================================

                var lockRecord =
                    new EmployeeDeviceLock
                    {
                        Id = Guid.NewGuid(),

                        UserId =
                            userId,

                        DeviceId =
                            deviceId,

                        CreatedAtUtc =
                            DateTime.UtcNow,

                        LastSeenAtUtc =
                            DateTime.UtcNow
                    };


                db.EmployeeDeviceLocks
                    .Add(lockRecord);


                await db.SaveChangesAsync();


                // ====================================================
                // VERIFY
                // ====================================================

                var verification =
                    await db.EmployeeDeviceLocks
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId);


                if (
                    verification == null ||
                    !string.Equals(
                        verification.DeviceId,
                        deviceId,
                        StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync();

                    _logger.LogError(
                        "DEVICE LOCK VERIFICATION FAILED. UserId={UserId}",
                        userId);

                    return false;
                }


                // ====================================================
                // COMMIT
                // ====================================================

                await transaction.CommitAsync();


                _logger.LogInformation(
                    "DEVICE LOCK CREATED. UserId={UserId}, DeviceId={DeviceId}",
                    userId,
                    deviceId);


                return true;
            }
            catch (DbUpdateException ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }

                _logger.LogWarning(
                    ex,
                    "DEVICE LOCK INSERT FAILED. UserId={UserId}",
                    userId);

                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }

                _logger.LogError(
                    ex,
                    "DEVICE LOCK ERROR. UserId={UserId}",
                    userId);

                return false;
            }
        }


        // ============================================================
        // RELEASE LOCK IF IDENTITY LOGIN FAILS
        // ============================================================

        private async Task
            ReleaseEmployeeDeviceLockAsync(
                string userId,
                string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

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
        }
    }
}