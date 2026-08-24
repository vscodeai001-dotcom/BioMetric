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
}
