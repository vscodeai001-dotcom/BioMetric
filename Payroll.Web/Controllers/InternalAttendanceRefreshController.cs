using Microsoft.AspNetCore.Mvc;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/internal/attendance-refresh")]
public sealed class InternalAttendanceRefreshController : ControllerBase
{
    private readonly AttendanceRefreshService _refreshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalAttendanceRefreshController> _logger;

    public InternalAttendanceRefreshController(
        AttendanceRefreshService refreshService,
        IConfiguration configuration,
        ILogger<InternalAttendanceRefreshController> logger)
    {
        _refreshService = refreshService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Refresh(
        [FromHeader(Name = "X-Attendance-Refresh-Secret")]
        string? secret)
    {
        var expectedSecret =
            _configuration["AttendanceRefresh:WorkerSecret"];

        if (string.IsNullOrWhiteSpace(expectedSecret) ||
            string.IsNullOrWhiteSpace(secret) ||
            !string.Equals(
                secret,
                expectedSecret,
                StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        await _refreshService.NotifyAllDataChangedAsync();

        _logger.LogInformation(
            "Attendance Worker refresh notification accepted.");

        return Ok();
    }

    [HttpPost("/api/internal/gps-session-ended")]
    public async Task<IActionResult> GpsSessionEnded(
        [FromHeader(Name = "X-Attendance-Refresh-Secret")] string? secret,
        [FromBody] IReadOnlyCollection<GpsSessionEndedRequest>? sessions)
    {
        var expectedSecret = _configuration["AttendanceRefresh:WorkerSecret"];

        if (string.IsNullOrWhiteSpace(expectedSecret) ||
            string.IsNullOrWhiteSpace(secret) ||
            !string.Equals(secret, expectedSecret, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        if (sessions == null || sessions.Count == 0)
            return Ok();

        var valid = sessions
            .Where(x => x.EmployeeId > 0 && x.SessionId != Guid.Empty)
            .Select(x => new AttendanceRefreshService.GpsSessionEndNotification(
                x.EmployeeId,
                x.SessionId,
                x.EndedAtUtc,
                string.IsNullOrWhiteSpace(x.EndReason) ? "ENDED" : x.EndReason))
            .ToList();

        await _refreshService.NotifyGpsSessionsEndedAsync(valid);
        return Ok();
    }

    public sealed class GpsSessionEndedRequest
    {
        public int EmployeeId { get; set; }
        public Guid SessionId { get; set; }
        public DateTime EndedAtUtc { get; set; }
        public string EndReason { get; set; } = string.Empty;
    }

}
