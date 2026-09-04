using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payroll.Shared.Data;

namespace Payroll.AttendanceService.Services;

/// <summary>
/// Background service that monitors and cleans up stale GPS sessions.
///
/// RESPONSIBILITIES:
/// ================================================================
/// 1. End GPS sessions for employees with no active device lock
/// 2. Mark GPS sessions as timed-out if no updates for 30 minutes
/// 3. Ensure database state stays synchronized with device locks
///
/// RUN INTERVAL: Every 60 seconds
///
/// SCENARIOS HANDLED:
/// ================================================================
/// 1. Browser Circuit Disposed / Page Reload
///    - Employee still logged in (device lock exists)
///    - GPS session still active in database
///    - No updates received for 30+ minutes
///    ? Mark as TIMED_OUT
///
/// 2. Force Logout from Another Device
///    - Device lock removed by force logout code
///    - GPS session still active in database
///    ? End session immediately with FORCE_LOGGED_OUT reason
///
/// 3. Manual Logout
///    - Device lock removed by logout
///    - GPS session already ended by logout code
///    ? Verify consistency (should already be ended)
/// ================================================================
/// </summary>
public class GpsSessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GpsSessionCleanupService> _logger;
    private readonly int _checkIntervalSeconds;

    // Timeout policy constants
    private const int NoUpdateTimeoutSeconds = 1800; // 30 minutes

    public GpsSessionCleanupService(
        IServiceProvider serviceProvider,
        ILogger<GpsSessionCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _checkIntervalSeconds = configuration.GetValue<int>(
            "GpsSessionCleanup:CheckIntervalSeconds",
            60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GPS Session Cleanup Service starting. Check interval: {IntervalSeconds} seconds",
            _checkIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GPS Session Cleanup Service encountered an error");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_checkIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("GPS Session Cleanup Service stopped");
    }

    private async Task CleanupStaleSessionsAsync(
        CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // ================================================================
            // PHASE 1: END SESSIONS FOR EMPLOYEES WITH NO DEVICE LOCK
            // ================================================================
            //
            // If an employee has NO device lock but an active GPS session,
            // it means they were forcefully logged out or manually logged out.
            //
            // The GPS session should be ended immediately.
            // ================================================================

            await EndSessionsWithoutDeviceLockAsync(db, stoppingToken);

            // ================================================================
            // PHASE 2: MARK SESSIONS AS TIMED OUT (30+ minutes no updates)
            // ================================================================
            //
            // If a GPS session has been inactive for 30+ minutes AND the
            // employee still has a device lock (meaning they're still logged in),
            // mark it as timed out.
            //
            // This handles the case where:
            // - GPS watcher paused by browser power management
            // - Network issues causing GPS failure
            // - Browser minimized for long time
            // ================================================================

            await MarkTimedOutSessionsAsync(db, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cleanup GPS sessions");
        }
    }

    private async Task EndSessionsWithoutDeviceLockAsync(
        AppDbContext db,
        CancellationToken stoppingToken)
    {
        try
        {
            // Find all active GPS sessions
            var activeSessions = await db.EmployeeGpsSessions
                .Where(s => s.EndedAtUtc == null)
                .ToListAsync(stoppingToken);

            if (activeSessions.Count == 0)
                return;

            // Get all employee IDs from active sessions
            var employeeIds = activeSessions
                .Select(s => s.EmployeeId)
                .Distinct()
                .ToList();

            // Get all employees and their AspNetUserIds
            var employees = await db.Employees
                .AsNoTracking()
                .Where(e => employeeIds.Contains(e.EmployeeID))
                .Select(e => new { e.EmployeeID, e.AspNetUserId })
                .ToListAsync(stoppingToken);

            // Get all UserIds with active device locks
            var userIdsWithLocks = await db.EmployeeDeviceLocks
                .AsNoTracking()
                .Select(d => d.UserId)
                .Distinct()
                .ToListAsync(stoppingToken);

            var sessionsToEnd = new List<EmployeeGpsSession>();
            var now = DateTime.UtcNow;

            // Check each session
            foreach (var session in activeSessions)
            {
                // Find the employee's AspNetUserId
                var employee = employees.FirstOrDefault(e => e.EmployeeID == session.EmployeeId);

                if (employee?.AspNetUserId == null)
                    continue;

                // Check if this employee has a device lock
                var hasDeviceLock = userIdsWithLocks.Contains(employee.AspNetUserId);

                if (!hasDeviceLock)
                {
                    // No device lock = employee is NOT logged in
                    // End the GPS session
                    session.EndedAtUtc = now;
                    session.EndReason = "NO_DEVICE_LOCK";

                    sessionsToEnd.Add(session);

                    _logger.LogInformation(
                        "Ending GPS session - no device lock. EmployeeId={EmployeeId}, SessionId={SessionId}",
                        session.EmployeeId,
                        session.SessionId);
                }
            }

            if (sessionsToEnd.Count > 0)
            {
                await db.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "Ended {Count} GPS sessions due to missing device locks",
                    sessionsToEnd.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to end sessions without device locks");
        }
    }

    private async Task MarkTimedOutSessionsAsync(
        AppDbContext db,
        CancellationToken stoppingToken)
    {
        try
        {
            var timeoutBefore =
                DateTime.UtcNow.AddSeconds(-NoUpdateTimeoutSeconds);

            var sessionsToTimeout = await db.EmployeeGpsSessions
                .Where(s =>
                    s.EndedAtUtc == null &&
                    s.LastUpdateAtUtc <= timeoutBefore)
                .ToListAsync(stoppingToken);

            if (sessionsToTimeout.Count == 0)
                return;

            var now = DateTime.UtcNow;

            foreach (var session in sessionsToTimeout)
            {
                session.EndedAtUtc = now;
                session.EndReason = "TIMED_OUT";

                _logger.LogInformation(
                    "Marking GPS session as timed out. " +
                    "EmployeeId={EmployeeId}, SessionId={SessionId}, " +
                    "LastUpdate={LastUpdate}, Age={Age} minutes",
                    session.EmployeeId,
                    session.SessionId,
                    session.LastUpdateAtUtc,
                    (int)(now - session.LastUpdateAtUtc).TotalMinutes);
            }

            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "Marked {Count} GPS sessions as timed out",
                sessionsToTimeout.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to mark timed-out GPS sessions");
        }
    }
}

