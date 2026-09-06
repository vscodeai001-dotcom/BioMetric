using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using Payroll.Web.Hubs;
using Payroll.Web.Security;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/mobile/employee")]
public sealed class MobileEmployeeController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly MobileEmployeeTokenService _tokens;
    private readonly GeoLocationService _geo;
    private readonly IHubContext<AttendanceRefreshHub> _hub;
    private readonly ILogger<MobileEmployeeController> _logger;

    public MobileEmployeeController(
        UserManager<IdentityUser> userManager,
        IDbContextFactory<AppDbContext> dbFactory,
        MobileEmployeeTokenService tokens,
        GeoLocationService geo,
        IHubContext<AttendanceRefreshHub> hub,
        ILogger<MobileEmployeeController> logger)
    {
        _userManager = userManager;
        _dbFactory = dbFactory;
        _tokens = tokens;
        _geo = geo;
        _hub = hub;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] MobileLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId) || string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest(new { success = false, message = "Employee ID, password and device ID are required." });

        if (!int.TryParse(request.EmployeeId.Trim(), out var employeeId) || employeeId <= 0)
            return Unauthorized(new { success = false, code = "INVALID_CREDENTIALS", message = "Invalid employee ID or password." });

        await using var db = await _dbFactory.CreateDbContextAsync();
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeID == employeeId && !x.IsDeleted);

        if (employee == null || string.IsNullOrWhiteSpace(employee.AspNetUserId))
            return Unauthorized(new { success = false, code = "INVALID_CREDENTIALS", message = "Invalid employee ID or password." });

        var user = await _userManager.FindByIdAsync(employee.AspNetUserId);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Employee") ||
            await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "SuperAdmin") ||
            !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { success = false, code = "INVALID_CREDENTIALS", message = "Invalid employee ID or password." });
        }

        var existing = await db.EmployeeDeviceLocks.FirstOrDefaultAsync(x => x.UserId == user.Id);
        var sameDevice = existing != null && string.Equals(existing.DeviceId, request.DeviceId.Trim(), StringComparison.Ordinal);

        if (existing != null && !sameDevice && !request.ForceReplace)
        {
            return Conflict(new
            {
                success = false,
                code = "EXISTING_SESSION",
                message = "This employee is already logged in on another device.",
                activeSinceUtc = existing.CreatedAtUtc
            });
        }

        if (existing != null && !sameDevice)
        {
            var stampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
                return StatusCode(500, new { success = false, message = "Unable to replace the existing employee session." });

            db.EmployeeDeviceLocks.Remove(existing);
            await db.SaveChangesAsync();
            existing = null;
        }

        if (existing == null)
        {
            db.EmployeeDeviceLocks.Add(new EmployeeDeviceLock
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DeviceId = request.DeviceId.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        else
        {
            existing.LastSeenAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var token = _tokens.Create(user.Id, employee.EmployeeID, request.DeviceId.Trim());

        return Ok(new MobileLoginResponse
        {
            Success = true,
            Token = token,
            EmployeeId = employee.EmployeeID,
            Name = employee.Name,
            Email = employee.Email ?? user.Email ?? string.Empty,
            MonthlySalary = employee.MonthlySalary,
            PaidLeaveBalance = employee.PaidLeaveBalance,
            SickLeaveBalance = employee.SickLeaveBalance
        });
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Employee")]
    public async Task<IActionResult> Me()
    {
        var employeeId = GetEmployeeId();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeID == employeeId && !x.IsDeleted);
        if (employee == null) return NotFound(new { success = false, message = "Employee not found." });

        return Ok(new MobileLoginResponse
        {
            Success = true,
            EmployeeId = employee.EmployeeID,
            Name = employee.Name,
            Email = employee.Email ?? string.Empty,
            MonthlySalary = employee.MonthlySalary,
            PaidLeaveBalance = employee.PaidLeaveBalance,
            SickLeaveBalance = employee.SickLeaveBalance
        });
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Employee")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var lockRecord = await db.EmployeeDeviceLocks.FirstOrDefaultAsync(x => x.UserId == userId);
        if (lockRecord != null)
        {
            db.EmployeeDeviceLocks.Remove(lockRecord);
            await db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    [HttpPost("gps/start")]
    [Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Employee")]
    public async Task<IActionResult> StartGps([FromBody] GpsSessionRequest request)
    {
        var employeeId = GetEmployeeId();
        if (!Guid.TryParse(request.SessionId, out var sessionId) || sessionId == Guid.Empty)
            return BadRequest(new { success = false, message = "A valid GPS session ID is required." });

        var ok = await _geo.StartGpsSessionAsync(employeeId, sessionId);
        return ok
            ? Ok(new { success = true, sessionId })
            : Conflict(new { success = false, message = "Unable to start GPS session." });
    }

    [HttpPost("gps/update")]
    [Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Employee")]
    public async Task<IActionResult> UpdateGps([FromBody] GpsUpdateRequest request)
    {
        var employeeId = GetEmployeeId();
        if (!Guid.TryParse(request.SessionId, out var sessionId) || sessionId == Guid.Empty)
            return BadRequest(new { success = false, message = "A valid GPS session ID is required." });
        if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) ||
            request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return BadRequest(new { success = false, message = "Invalid GPS coordinates." });

        var distance = await _geo.GetDistanceFromOfficeAsync(request.Latitude, request.Longitude);
        if (!distance.Success)
            return StatusCode(500, new { success = false, message = distance.Message });

        var liveUpdated = LiveLocationStore.Update(employeeId, request.Latitude, request.Longitude,
            Math.Max(0, request.Accuracy), distance.DistanceMeters, distance.AllowedRadiusMeters,
            distance.IsWithinAllowedRadius, sessionId);
        if (!liveUpdated)
            return Conflict(new { success = false, message = "GPS session is no longer active." });

        await _geo.UpdateGpsSessionAsync(employeeId, sessionId, request.Latitude, request.Longitude,
            request.Accuracy, distance.DistanceMeters, distance.AllowedRadiusMeters, distance.IsWithinAllowedRadius);

        await _geo.SaveLocationHistoryAsync(employeeId, sessionId, request.Latitude, request.Longitude,
            distance.DistanceMeters, distance.AllowedRadiusMeters, distance.IsWithinAllowedRadius, request.Accuracy);

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var lockRecord = await db.EmployeeDeviceLocks.FirstOrDefaultAsync(x => x.UserId == userId);
            if (lockRecord != null) { lockRecord.LastSeenAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); }
        }

        await _hub.Clients.All.SendAsync("LocationChanged", new
        {
            EmployeeId = employeeId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = Math.Max(0, request.Accuracy),
            DistanceMeters = distance.DistanceMeters,
            AllowedRadiusMeters = distance.AllowedRadiusMeters,
            IsWithinAllowedRadius = distance.IsWithinAllowedRadius,
            SessionId = sessionId,
            LastUpdatedUtc = DateTime.UtcNow,
            Source = "Android"
        });

        return Ok(new
        {
            success = true,
            employeeId,
            distanceMeters = distance.DistanceMeters,
            allowedRadiusMeters = distance.AllowedRadiusMeters,
            isWithinAllowedRadius = distance.IsWithinAllowedRadius,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpPost("gps/end")]
    [Authorize(AuthenticationSchemes = "MobileBearer", Roles = "Employee")]
    public async Task<IActionResult> EndGps([FromBody] GpsSessionRequest request)
    {
        var employeeId = GetEmployeeId();
        if (!Guid.TryParse(request.SessionId, out var sessionId) || sessionId == Guid.Empty)
            return BadRequest(new { success = false, message = "A valid GPS session ID is required." });

        await _geo.EndGpsSessionAsync(employeeId, sessionId, "LOGGED_OUT");
        return Ok(new { success = true });
    }

    private int GetEmployeeId()
    {
        return int.TryParse(User.FindFirstValue("employee_id"), out var id) ? id : 0;
    }

    public sealed class MobileLoginRequest
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public bool ForceReplace { get; set; }
    }

    public sealed class MobileLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public int EmployeeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public decimal MonthlySalary { get; set; }
        public decimal PaidLeaveBalance { get; set; }
        public decimal SickLeaveBalance { get; set; }
    }

    public sealed class GpsSessionRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    public sealed class GpsUpdateRequest : GpsSessionRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public double Speed { get; set; }
        public long Timestamp { get; set; }
        public int BatteryLevel { get; set; }
    }
}
