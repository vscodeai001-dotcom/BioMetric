using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Web.Hubs;

namespace Payroll.Web.Services;

public class GeoLocationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<GeoLocationService> _logger;
    private readonly AttendanceRefreshService _refreshService;
    private readonly IHubContext<AttendanceRefreshHub> _hubContext;

    // Dual Attendance uses a small hysteresis band around the configured
    // geofence boundary. This prevents normal GPS noise from becoming
    // attendance events while leaving the existing configured radius intact.
    private const double GeofenceHysteresisMinimumMeters = 15d;
    private const double GeofenceHysteresisMaximumMeters = 50d;
    private const int AuthoritativePunchProtectionSeconds = 120;
    private const int FallbackReconciliationWindowSeconds = 300;
    private const long AttendanceAdvisoryLockNamespace = 0x504159524F4C4CL;
    private const long GpsSessionAdvisoryLockNamespace = 0x4750534C4F434BL;

    public GeoLocationService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<GeoLocationService> logger,
        AttendanceRefreshService refreshService,
        IHubContext<AttendanceRefreshHub> hubContext)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _refreshService = refreshService;
        _hubContext = hubContext;
    }

    // ================================================================
    // GET DISTANCE FROM OFFICE
    // ================================================================

    public async Task<GeoDistanceResult> GetDistanceFromOfficeAsync(
        double latitude,
        double longitude)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var features = await db.FeatureSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == 1);

        if (features == null ||
    (!features.EnableGeoFencing &&
     !features.EnableDualAttendance))
        {
            return new GeoDistanceResult
            {
                Success = false,
                Message = "Geo-fencing module is disabled."
            };
        }

        var company = await db.CompanySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingID == 1);

        if (company == null ||
            company.OfficeLatitude == 0 ||
            company.OfficeLongitude == 0)
        {
            return new GeoDistanceResult
            {
                Success = false,
                Message = "Office location not configured by Admin."
            };
        }

        if (!IsValidCoordinate(latitude, longitude))
        {
            return new GeoDistanceResult
            {
                Success = false,
                Message = "Invalid GPS coordinates received."
            };
        }

        var distance = CalculateDistance(
            latitude,
            longitude,
            company.OfficeLatitude,
            company.OfficeLongitude);

        return new GeoDistanceResult
        {
            Success = true,
            DistanceMeters = distance,
            AllowedRadiusMeters = company.GeoRadiusMeters
        };
    }

    // ================================================================
    // START GPS SESSION
    // ================================================================

    public async Task<bool> StartGpsSessionAsync(
        int employeeId,
        Guid sessionId)
    {
        if (employeeId <= 0 || sessionId == Guid.Empty)
            return false;

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var existing = await db.EmployeeGpsSessions
    .FirstOrDefaultAsync(x =>
        x.SessionId == sessionId);

            if (existing != null)
            {
                /*
                 * Existing active session.
                 *
                 * This is the normal browser reconnect scenario.
                 */
                if (!existing.EndedAtUtc.HasValue)
                {
                    return true;
                }

                /*
                 * The browser still had an old SessionId in sessionStorage,
                 * but that database session has already ended/timed out.
                 *
                 * The caller must create a new SessionId.
                 */
                return false;
            }

            /*
             * Close any previous unfinished session for this employee.
             *
             * This protects against an old browser/circuit remaining
             * open when a completely new login starts.
             */
            var previousSessions = await db.EmployeeGpsSessions
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.EndedAtUtc == null &&
                    x.SessionId != sessionId)
                .ToListAsync();

            var now = DateTime.UtcNow;

            foreach (var previous in previousSessions)
            {
                previous.EndedAtUtc = now;
                previous.EndReason = "NEW_SESSION";
                // Ensure previous in-memory entries are removed so admins don't see old sessions
                try
                {
                    LiveLocationStore.Remove(previous.EmployeeId, previous.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove previous LiveLocationStore entry for employee {EmployeeId}", previous.EmployeeId);
                }
            }

            var session = new EmployeeGpsSession
            {
                EmployeeId = employeeId,
                SessionId = sessionId,
                StartedAtUtc = now,
                LastUpdateAtUtc = now,
                EndedAtUtc = null,
                EndReason = null,
                TotalPoints = 0,
                TotalDistanceMeters = 0,
                AverageAccuracyMeters = null
            };

            db.EmployeeGpsSessions.Add(session);

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "GPS session started. EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);

            /*
             * BROADCAST SESSION START
             *
             * Notify all connected admin clients that a new GPS session started.
             */
            try
            {
                await _hubContext.Clients.All.SendAsync(
                    "SessionStarted",
                    new
                    {
                        EmployeeId = employeeId,
                        SessionId = sessionId,
                        StartedAtUtc = now
                    });
            }
            catch (Exception signalREx)
            {
                _logger.LogWarning(
                    signalREx,
                    "Failed to broadcast session start via SignalR for employee {EmployeeId}",
                    employeeId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start GPS session. EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);

            return false;
        }
    }

    // ================================================================
    // UPDATE GPS SESSION
    // ================================================================

    public async Task<bool> UpdateGpsSessionAsync(
        int employeeId,
        Guid sessionId,
        double latitude,
        double longitude,
        double accuracyMeters,
        double distanceMeters,
        int allowedRadiusMeters,
        bool isWithinAllowedRadius)
    {
        if (employeeId <= 0 ||
            sessionId == Guid.Empty ||
            !IsValidCoordinate(latitude, longitude))
        {
            return false;
        }

        try
        {
            // The first update after login can race the session-start call.
            // Recover the session before taking the lifecycle lock, then
            // re-read it under the lock before touching LiveLocationStore.
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var session = await db.EmployeeGpsSessions
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.SessionId == sessionId);

            if (session == null)
            {
                var created = await StartGpsSessionAsync(
                    employeeId,
                    sessionId);

                if (!created)
                    return false;

                session = await db.EmployeeGpsSessions
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == employeeId &&
                        x.SessionId == sessionId);

                if (session == null)
                    return false;
            }

            // Session-level PostgreSQL advisory lock coordinates GPS updates
            // and logout/session-end operations across Web/Worker instances.
            // This closes the race where an old GPS request could repopulate
            // LiveLocationStore immediately after logout.
            await db.Database.OpenConnectionAsync();
            var lockKey = GpsSessionAdvisoryLockNamespace + (uint)employeeId;
            var lockHeld = false;

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock({0})",
                    lockKey);
                lockHeld = true;

                session = await db.EmployeeGpsSessions
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == employeeId &&
                        x.SessionId == sessionId);

                if (session == null || session.EndedAtUtc.HasValue)
                {
                    // An old in-flight GPS request is no longer authoritative.
                    // Remove only if this exact old session still owns the
                    // in-memory entry. A newer session is never removed.
                    LiveLocationStore.Remove(employeeId, sessionId);
                    return false;
                }

                var safeAccuracy = NormalizeAccuracy(accuracyMeters);
                var safeDistance = NormalizeDistance(distanceMeters);
                var now = DateTime.UtcNow;
                var previousLocationState = session.LastIsWithinAllowedRadius;

                var stableLocationState = ResolveStableGeofenceState(
                    previousLocationState,
                    safeDistance,
                    allowedRadiusMeters);

                // Automatic attendance is evaluated while the session lock is
                // held, so logout cannot interleave with the decision.
                if (stableLocationState.HasValue)
                {
                    var attendanceEvaluationCompleted =
                        await ProcessAutomaticGeofencePunchAsync(
                            db,
                            employeeId,
                            sessionId,
                            latitude,
                            longitude,
                            safeAccuracy,
                            safeDistance,
                            allowedRadiusMeters,
                            previousLocationState,
                            stableLocationState.Value);

                    // Persist the SAME stable state that drove the attendance
                    // decision only when the evaluation completed safely. If
                    // automatic attendance failed, keep the previous state so
                    // the next valid GPS fix retries instead of silently
                    // consuming the transition. Never overwrite it with raw
                    // GPS state.
                    if (attendanceEvaluationCompleted)
                    {
                        session.LastIsWithinAllowedRadius =
                            stableLocationState.Value;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Dual Attendance evaluation did not complete. Geofence state was not advanced so the transition can be retried. EmployeeId={EmployeeId}, SessionId={SessionId}",
                            employeeId,
                            sessionId);
                    }
                }

                session.LastUpdateAtUtc = now;
                session.LastLatitude = latitude;
                session.LastLongitude = longitude;
                session.LastAccuracyMeters = safeAccuracy;
                session.LastDistanceFromOfficeMeters = safeDistance;
                session.LastAllowedRadiusMeters =
                    allowedRadiusMeters < 0 ? 0 : allowedRadiusMeters;

                session.TotalPoints++;
                session.TotalDistanceMeters += safeDistance;

                if (session.TotalPoints == 1)
                {
                    session.AverageAccuracyMeters = safeAccuracy;
                }
                else
                {
                    var previousAverage =
                        session.AverageAccuracyMeters ?? 0;

                    session.AverageAccuracyMeters =
                        ((previousAverage * (session.TotalPoints - 1)) +
                         safeAccuracy) /
                        session.TotalPoints;
                }

                // Only after the active-session check and state update do we
                // publish the location into the process-local live store.
                var liveUpdated = LiveLocationStore.Update(
                    employeeId,
                    latitude,
                    longitude,
                    safeAccuracy,
                    safeDistance,
                    allowedRadiusMeters,
                    isWithinAllowedRadius,
                    sessionId);

                if (!liveUpdated)
                {
                    _logger.LogWarning(
                        "GPS live-store update rejected. EmployeeId={EmployeeId}, SessionId={SessionId}",
                        employeeId,
                        sessionId);
                    return false;
                }

                await db.SaveChangesAsync();

                try
                {
                    await _hubContext.Clients.All.SendAsync(
                        "LocationChanged",
                        new
                        {
                            EmployeeId = employeeId,
                            SessionId = sessionId,
                            Latitude = latitude,
                            Longitude = longitude,
                            Timestamp = now,
                            DistanceMeters = safeDistance,
                            AccuracyMeters = safeAccuracy,
                            IsWithinAllowedRadius = isWithinAllowedRadius
                        });
                }
                catch (Exception signalREx)
                {
                    _logger.LogWarning(
                        signalREx,
                        "Failed to broadcast location update via SignalR for employee {EmployeeId}",
                        employeeId);
                }

                return true;
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_unlock({0})",
                            lockKey);
                    }
                    catch (Exception unlockEx)
                    {
                        _logger.LogWarning(
                            unlockEx,
                            "Failed to release GPS session advisory lock. EmployeeId={EmployeeId}",
                            employeeId);
                    }
                }

                await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update GPS session. EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);

            return false;
        }
    }


    // ================================================================
    // AUTOMATIC GEOFENCE ATTENDANCE FALLBACK
    // ================================================================
    //
    // Geofence attendance is a FALLBACK only.
    //
    // ENTER radius  -> automatic IN, only when the current attendance
    //                   state is OUT.
    // EXIT radius   -> automatic OUT, only when the current attendance
    //                   state is IN.
    //
    // Physical biometric punches remain the priority source. If a
    // biometric punch arrives shortly after an automatic geofence punch,
    // the Attendance Worker reconciles the temporary geofence punch and
    // keeps the biometric punch.
    // ================================================================

    private async Task<bool> ProcessAutomaticGeofencePunchAsync(
        AppDbContext db,
        int employeeId,
        Guid sessionId,
        double latitude,
        double longitude,
        double accuracyMeters,
        double distanceMeters,
        int allowedRadiusMeters,
        bool? previousLocationState,
        bool currentLocationState)
    {
        // Automatic geofence attendance is available ONLY in Dual Attendance mode.
        // Single Geo-Fencing mode remains manual mobile punch only.
        var features = await db.FeatureSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == 1);

        if (features?.EnableDualAttendance != true)
            return true;

        // A first GPS fix establishes location state but is not itself a
        // transition unless the fix is clearly inside the configured radius.
        var previousState = previousLocationState ?? false;

        // A brand-new GPS session that starts OUTSIDE does not prove that
        // the employee just left the office. Do not invent an OUT punch.
        // Existing sessions still produce normal INSIDE -> OUTSIDE exits.
        if (!previousLocationState.HasValue &&
            !currentLocationState)
        {
            return true;
        }

        if (previousLocationState.HasValue &&
            previousState == currentLocationState)
        {
            return true;
        }

        if (allowedRadiusMeters <= 0)
            return true;

        try
        {
            // Keep the attendance decision and fallback punch atomic.
            // The surrounding GPS/session advisory lock prevents logout and
            // stale GPS updates from interleaving with this operation.
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            await AcquireAttendanceAdvisoryLockAsync(
                db,
                employeeId);

            var indiaNow = GetIndiaNow();

            var businessDayStart =
                DateTime.SpecifyKind(
                    indiaNow.Date,
                    DateTimeKind.Unspecified);

            var businessDayEnd =
                DateTime.SpecifyKind(
                    indiaNow.Date.AddDays(1),
                    DateTimeKind.Unspecified);

            var todaysPunches =
                await db.AttendanceLogs
                    .Where(x =>
                        x.EmployeeID == employeeId &&
                        x.PunchTime >= businessDayStart &&
                        x.PunchTime < businessDayEnd)
                    .OrderBy(x => x.PunchTime)
                    .ThenBy(x => x.LogID)
                    .ToListAsync();

            // The existing attendance engine remains untouched: attendance
            // state is still derived from chronological punch parity.
            var attendanceCurrentlyOpen =
                todaysPunches.Count % 2 != 0;

            var requiredPunchType =
                currentLocationState
                    ? "IN"
                    : "OUT";

            // GPS location alone never creates an OUT for an employee who is
            // already OUT, nor an IN for an employee who is already IN.
            if (currentLocationState && attendanceCurrentlyOpen)
                return true;

            if (!currentLocationState && !attendanceCurrentlyOpen)
                return true;

            /*
             * BIOMETRIC and explicit MOBILE punches are authoritative.
             * If one has already been committed close to this transition,
             * the fallback must not add another event.
             */
            var recentAuthoritative =
                todaysPunches
                    .Where(IsAuthoritativeAttendancePunch)
                    .Where(x =>
                        Math.Abs(
                            (x.PunchTime - indiaNow).TotalSeconds)
                        <= AuthoritativePunchProtectionSeconds)
                    .OrderByDescending(x => x.PunchTime)
                    .FirstOrDefault();

            if (recentAuthoritative != null)
            {
                _logger.LogInformation(
                    "Automatic geofence {PunchType} skipped because an authoritative attendance punch already exists. " +
                    "EmployeeId={EmployeeId}, LogId={LogId}, Device={Device}, Time={PunchTime}",
                    requiredPunchType,
                    employeeId,
                    recentAuthoritative.LogID,
                    recentAuthoritative.DeviceID,
                    recentAuthoritative.PunchTime);

                await transaction.CommitAsync();
                return true;
            }

            var log =
                new AttendanceLog
                {
                    EmployeeID = employeeId,
                    BiometricID = "GEOFENCE_AUTO",
                    PunchTime = indiaNow,
                    DeviceID = "GeofenceAuto",
                    LogType = requiredPunchType,
                    Latitude = latitude,
                    Longitude = longitude,
                    IsApproved = true
                };

            db.AttendanceLogs.Add(log);
            await db.SaveChangesAsync();

            var result =
                new GeoPunchResult
                {
                    Success = true,
                    Message =
                        $"Automatic geofence {requiredPunchType} recorded. " +
                        $"(Dist: {distanceMeters:F0}m)"
                };

            await SavePunchAuditAsync(
                db,
                employeeId,
                sessionId,
                DateTime.UtcNow,
                latitude,
                longitude,
                accuracyMeters,
                distanceMeters,
                allowedRadiusMeters,
                true,
                result,
                log.LogID,
                "GEOFENCE_AUTO");

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Automatic geofence {PunchType} recorded. " +
                "EmployeeId={EmployeeId}, LogId={LogId}, Distance={Distance}m",
                requiredPunchType,
                employeeId,
                log.LogID,
                Math.Round(distanceMeters, 1));

            try
            {
                await _refreshService.NotifyDataChangedAsync(
                    employeeId,
                    DateOnly.FromDateTime(log.PunchTime.Date),
                    "GEOFENCE_AUTO");

                await _refreshService.NotifyAttendanceChangedAsync(
                    employeeId,
                    DateOnly.FromDateTime(log.PunchTime.Date));

                await _refreshService.NotifyPunchCreatedAsync(
                    employeeId,
                    DateOnly.FromDateTime(log.PunchTime.Date));
            }
            catch (Exception refreshEx)
            {
                _logger.LogWarning(
                    refreshEx,
                    "Automatic geofence punch saved but attendance refresh notification failed. " +
                    "EmployeeId={EmployeeId}, LogId={LogId}",
                    employeeId,
                    log.LogID);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Automatic fallback must NEVER break normal GPS tracking.
            // Record the failure separately so an operator can diagnose a
            // missing automatic punch from the existing Punch Audit screen.
            _logger.LogError(
                ex,
                "Automatic geofence attendance processing failed. " +
                "EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);

            try
            {
                await using var auditDb =
                    await _dbFactory.CreateDbContextAsync();

                await SavePunchAuditAsync(
                    auditDb,
                    employeeId,
                    sessionId,
                    DateTime.UtcNow,
                    latitude,
                    longitude,
                    accuracyMeters,
                    distanceMeters,
                    allowedRadiusMeters,
                    currentLocationState,
                    new GeoPunchResult
                    {
                        Success = false,
                        Message = $"Automatic geofence {
                            (currentLocationState ? "IN" : "OUT")} failed: {ex.Message}"
                    },
                    null,
                    "GEOFENCE_AUTO");
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(
                    auditEx,
                    "Failed to record automatic geofence failure audit. EmployeeId={EmployeeId}, SessionId={SessionId}",
                    employeeId,
                    sessionId);
            }

            return false;
        }
    }

    private static bool IsAuthoritativeAttendancePunch(AttendanceLog log)
    {
        if (log == null || !log.IsApproved)
            return false;

        var device = log.DeviceID?.Trim() ?? string.Empty;
        var biometricId = log.BiometricID?.Trim() ?? string.Empty;

        if (device.Equals("GeofenceAuto", StringComparison.OrdinalIgnoreCase) ||
            biometricId.Equals("GEOFENCE_AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return
            device.StartsWith("ZKTeco_", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("MobileWeb", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("Android", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AcquireAttendanceAdvisoryLockAsync(
        AppDbContext db,
        int employeeId)
    {
        var lockKey =
            AttendanceAdvisoryLockNamespace +
            (uint)employeeId;

        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})",
            lockKey);
    }

    private static double GetGeofenceHysteresisMeters(
        int allowedRadiusMeters)
    {
        var calculated =
            allowedRadiusMeters * 0.10d;

        return Math.Clamp(
            calculated,
            GeofenceHysteresisMinimumMeters,
            GeofenceHysteresisMaximumMeters);
    }

    private static bool? ResolveStableGeofenceState(
        bool? previousState,
        double distanceMeters,
        int allowedRadiusMeters)
    {
        if (allowedRadiusMeters <= 0 ||
            !double.IsFinite(distanceMeters))
        {
            return null;
        }

        var hysteresis =
            GetGeofenceHysteresisMeters(allowedRadiusMeters);

        var enterBoundary =
            Math.Max(0d, allowedRadiusMeters - hysteresis);

        var exitBoundary =
            allowedRadiusMeters + hysteresis;

        if (previousState == true)
        {
            // Once inside, remain inside until clearly beyond the exit
            // boundary. This suppresses GPS oscillation around the radius.
            return distanceMeters >= exitBoundary
                ? false
                : true;
        }

        if (previousState == false)
        {
            // Once outside, remain outside until clearly within the entry
            // boundary. This also makes repeated GPS fixes idempotent.
            return distanceMeters <= enterBoundary
                ? true
                : false;
        }

        // First fix: only establish a state when it is clearly classified.
        if (distanceMeters <= enterBoundary)
            return true;

        if (distanceMeters >= exitBoundary)
            return false;

        return null;
    }

    private static bool IsBiometricPunch(
        AttendanceLog log)
    {
        if (log == null)
            return false;

        var device = log.DeviceID?.Trim() ?? string.Empty;
        var biometricId = log.BiometricID?.Trim() ?? string.Empty;

        return device.StartsWith(
                   "ZKTeco_",
                   StringComparison.OrdinalIgnoreCase)
               ||
               (
                   !string.IsNullOrWhiteSpace(biometricId) &&
                   !biometricId.Equals(
                       "MOBILE_APP",
                       StringComparison.OrdinalIgnoreCase) &&
                   !biometricId.Equals(
                       "GEOFENCE_AUTO",
                       StringComparison.OrdinalIgnoreCase) &&
                   !biometricId.StartsWith(
                       "ANDROID-",
                       StringComparison.OrdinalIgnoreCase) &&
                   !device.Equals(
                       "MobileWeb",
                       StringComparison.OrdinalIgnoreCase) &&
                   !device.Equals(
                       "Android",
                       StringComparison.OrdinalIgnoreCase) &&
                   !device.Equals(
                       "GeofenceAuto",
                       StringComparison.OrdinalIgnoreCase)
               );
    }

    // ================================================================
    // END GPS SESSION
    // ================================================================

    public async Task EndGpsSessionAsync(
        int employeeId,
        Guid sessionId,
        string endReason = "LOGGED_OUT")
    {
        if (employeeId <= 0 || sessionId == Guid.Empty)
            return;

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            await db.Database.OpenConnectionAsync();
            var lockKey = GpsSessionAdvisoryLockNamespace + (uint)employeeId;
            var lockHeld = false;

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock({0})",
                    lockKey);
                lockHeld = true;

                var session = await db.EmployeeGpsSessions
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == employeeId &&
                        x.SessionId == sessionId);

                if (session == null)
                {
                    LiveLocationStore.Remove(employeeId, sessionId);
                    return;
                }

                if (session.EndedAtUtc.HasValue)
                {
                    LiveLocationStore.Remove(employeeId, sessionId);
                    return;
                }

                session.EndedAtUtc = DateTime.UtcNow;
                session.EndReason =
                    string.IsNullOrWhiteSpace(endReason)
                        ? "ENDED"
                        : endReason.Length > 40
                            ? endReason[..40]
                            : endReason;

                await db.SaveChangesAsync();

                // Remove only the session being ended. A newer login/session
                // can never be removed by an old logout request.
                LiveLocationStore.Remove(employeeId, sessionId);

                _logger.LogInformation(
                    "GPS session ended. EmployeeId={EmployeeId}, SessionId={SessionId}, Reason={Reason}",
                    employeeId,
                    sessionId,
                    session.EndReason);

                try
                {
                    await _hubContext.Clients.All.SendAsync(
                        "SessionEnded",
                        new
                        {
                            EmployeeId = employeeId,
                            SessionId = sessionId,
                            EndedAtUtc = session.EndedAtUtc,
                            EndReason = session.EndReason
                        });
                }
                catch (Exception signalREx)
                {
                    _logger.LogWarning(
                        signalREx,
                        "Failed to broadcast session end via SignalR for employee {EmployeeId}",
                        employeeId);
                }
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_unlock({0})",
                            lockKey);
                    }
                    catch (Exception unlockEx)
                    {
                        _logger.LogWarning(
                            unlockEx,
                            "Failed to release GPS session advisory lock after logout. EmployeeId={EmployeeId}",
                            employeeId);
                    }
                }

                await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to end GPS session. EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);
        }
    }


    // ================================================================
    // MARK SESSION TIMED OUT
    // ================================================================
    //
    // IMPORTANT SESSION TIMEOUT POLICY:
    //
    // GPS sessions should ONLY timeout after VERY long inactivity.
    //
    // Reasons:
    // 1. Employee may have GPS disabled but still be logged in
    // 2. Network interruptions are temporary
    // 3. GPS watcher may be paused by browser power management
    // 4. Employee is still working even without GPS updates
    //
    // Only mark a session as timed-out if:
    // - No GPS update for 30 minutes (1800 seconds)
    // - Session is still marked as active in database
    //
    // This allows the admin to manually log out an employee
    // or for a new login to invalidate the old session.
    // ================================================================

    public async Task MarkTimedOutSessionsAsync()
    {
        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            // Only timeout sessions with NO updates for 30 minutes
            var timeoutBefore =
                DateTime.UtcNow.AddSeconds(-1800);

            var sessions = await db.EmployeeGpsSessions
                .Where(x =>
                    x.EndedAtUtc == null &&
                    x.LastUpdateAtUtc <= timeoutBefore)
                .ToListAsync();

            if (sessions.Count == 0)
                return;

            var now = DateTime.UtcNow;

            foreach (var session in sessions)
            {
                session.EndedAtUtc = now;
                session.EndReason = "TIMED_OUT";

                // Remove only the timed-out session. A newer session for the
                // same employee can never be removed by this cleanup pass.
                LiveLocationStore.Remove(session.EmployeeId, session.SessionId);

                try
                {
                    await _hubContext.Clients.All.SendAsync(
                        "SessionEnded",
                        new
                        {
                            EmployeeId = session.EmployeeId,
                            SessionId = session.SessionId,
                            EndedAtUtc = now,
                            EndReason = session.EndReason
                        });
                }
                catch (Exception signalREx)
                {
                    _logger.LogWarning(
                        signalREx,
                        "Failed to broadcast timed-out GPS session for employee {EmployeeId}",
                        session.EmployeeId);
                }

                _logger.LogInformation(
                    "GPS session timed out. EmployeeId={EmployeeId}, SessionId={SessionId}, " +
                    "LastUpdate={LastUpdate}, Age={Age} minutes",
                    session.EmployeeId,
                    session.SessionId,
                    session.LastUpdateAtUtc,
                    (int)(now - session.LastUpdateAtUtc).TotalMinutes);
            }

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Marked {Count} GPS sessions as timed out.",
                sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to mark timed-out GPS sessions.");
        }
    }

    // ================================================================
    // GET ACTIVE SESSION
    // ================================================================

    public async Task<EmployeeGpsSession?> GetActiveGpsSessionAsync(
        int employeeId)
    {
        if (employeeId <= 0)
            return null;

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            return await db.EmployeeGpsSessions
                .AsNoTracking()
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.EndedAtUtc == null)
                .OrderByDescending(x => x.StartedAtUtc)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get active GPS session. EmployeeId={EmployeeId}",
                employeeId);

            return null;
        }
    }

    // ================================================================
    // SAVE EMPLOYEE LOCATION HISTORY
    // ================================================================

    public async Task SaveLocationHistoryAsync(
        int employeeId,
        Guid sessionId,
        double latitude,
        double longitude,
        double distanceMeters,
        int allowedRadiusMeters,
        bool isWithinAllowedRadius,
        double accuracyMeters = 0)
    {
        if (employeeId <= 0 ||
            sessionId == Guid.Empty ||
            !IsValidCoordinate(latitude, longitude))
        {
            return;
        }

        var safeAccuracy =
            NormalizeAccuracy(accuracyMeters);

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var record = new EmployeeLocationHistory
            {
                EmployeeId = employeeId,
                SessionId = sessionId,
                Latitude = latitude,
                Longitude = longitude,
                AccuracyMeters = safeAccuracy,
                DistanceFromOfficeMeters = NormalizeDistance(distanceMeters),
                AllowedRadiusMeters =
                    allowedRadiusMeters < 0
                        ? 0
                        : allowedRadiusMeters,
                IsWithinAllowedRadius = isWithinAllowedRadius,
                RecordedAtUtc = DateTime.UtcNow
            };

            db.EmployeeLocationHistory.Add(record);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            /*
             * GPS history failure must NEVER stop live tracking.
             */
            _logger.LogError(
                ex,
                "Failed to save GPS history for employee {EmployeeId}",
                employeeId);
        }
    }

    // ================================================================
    // PROCESS MOBILE PUNCH
    // ================================================================

    public async Task<GeoPunchResult> ProcessMobilePunchAsync(
        int employeeId,
        double lat,
        double lon,
        double accuracyMeters = 0,
        Guid? sessionId = null)
    {
        var auditTimeUtc =
            DateTime.UtcNow;

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        var company = await db.CompanySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.SettingID == 1);

        var features = await db.FeatureSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.Id == 1);

        var safeAccuracy =
            NormalizeAccuracy(accuracyMeters);

        if (features?.EnableGeoFencing != true && features?.EnableDualAttendance != true)
        {
            var result = new GeoPunchResult
            {
                Success = false,
                Message = "Geo-fencing module is disabled."
            };

            await SavePunchAuditAsync(
                db,
                employeeId,
                sessionId,
                auditTimeUtc,
                lat,
                lon,
                safeAccuracy,
                0,
                0,
                false,
                result);

            return result;
        }

        if (company == null ||
            company.OfficeLatitude == 0 ||
            company.OfficeLongitude == 0)
        {
            var result = new GeoPunchResult
            {
                Success = false,
                Message =
                    "Office location not configured by Admin."
            };

            await SavePunchAuditAsync(
                db,
                employeeId,
                sessionId,
                auditTimeUtc,
                lat,
                lon,
                safeAccuracy,
                0,
                company?.GeoRadiusMeters ?? 0,
                false,
                result);

            return result;
        }

        if (!IsValidCoordinate(lat, lon))
        {
            var result = new GeoPunchResult
            {
                Success = false,
                Message =
                    "Invalid GPS coordinates received."
            };

            await SavePunchAuditAsync(
                db,
                employeeId,
                sessionId,
                auditTimeUtc,
                lat,
                lon,
                safeAccuracy,
                0,
                company.GeoRadiusMeters,
                false,
                result);

            return result;
        }

        var distance = CalculateDistance(
            lat,
            lon,
            company.OfficeLatitude,
            company.OfficeLongitude);

        var withinRadius =
            distance <= company.GeoRadiusMeters;

        if (!withinRadius)
        {
            var result = new GeoPunchResult
            {
                Success = false,
                Message =
                    $"You are {distance:F0} meters away. " +
                    $"Allowed radius is " +
                    $"{company.GeoRadiusMeters}m."
            };

            await SavePunchAuditAsync(
                db,
                employeeId,
                sessionId,
                auditTimeUtc,
                lat,
                lon,
                safeAccuracy,
                distance,
                company.GeoRadiusMeters,
                false,
                result);

            return result;
        }

        await using var attendanceTransaction =
            await db.Database.BeginTransactionAsync();

        await AcquireAttendanceAdvisoryLockAsync(
            db,
            employeeId);

        var log = new AttendanceLog
        {
            EmployeeID = employeeId,
            PunchTime = GetIndiaNow(),
            BiometricID = "MOBILE_APP",
            DeviceID = "MobileWeb",
            LogType = "Punch",
            Latitude = lat,
            Longitude = lon,
            IsApproved = true
        };

        db.AttendanceLogs.Add(log);

        if (sessionId.HasValue &&
            sessionId.Value != Guid.Empty)
        {
            db.EmployeeLocationHistory.Add(
                new EmployeeLocationHistory
                {
                    EmployeeId = employeeId,
                    SessionId = sessionId.Value,
                    Latitude = lat,
                    Longitude = lon,
                    AccuracyMeters = safeAccuracy,
                    DistanceFromOfficeMeters = NormalizeDistance(distance),
                    AllowedRadiusMeters = company.GeoRadiusMeters,
                    IsWithinAllowedRadius = true,
                    RecordedAtUtc = auditTimeUtc
                });
        }

        await db.SaveChangesAsync();

        var success = new GeoPunchResult
        {
            Success = true,
            Message =
                $"Punch accepted! (Dist: {distance:F0}m)"
        };

        await SavePunchAuditAsync(
            db,
            employeeId,
            sessionId,
            auditTimeUtc,
            lat,
            lon,
            safeAccuracy,
            distance,
            company.GeoRadiusMeters,
            true,
            success,
            log.LogID);

        await ReconcileAutomaticFallbackAsync(
            db,
            employeeId,
            log.PunchTime);

        await attendanceTransaction.CommitAsync();

        try
        {
            await _refreshService.NotifyDataChangedAsync(
                employeeId,
                DateOnly.FromDateTime(log.PunchTime.Date),
                "MOBILE_PUNCH");

            await _refreshService
                .NotifyAttendanceChangedAsync(
                    employeeId,
                    DateOnly.FromDateTime(
                        log.PunchTime.Date));

            await _refreshService.NotifyPunchCreatedAsync(
                employeeId,
                DateOnly.FromDateTime(log.PunchTime.Date));
        }
        catch (Exception ex)
        {
            // Refresh notification must never invalidate a successful punch.
            _logger.LogWarning(
                ex,
                "Mobile punch saved but live attendance notification failed for employee {EmployeeId}.",
                employeeId);
        }

        return success;
    }

    // ================================================================
    // AUTHORITATIVE MOBILE PUNCH RECONCILIATION
    // ================================================================

    public async Task ReconcileAutomaticFallbackAsync(
        AppDbContext db,
        int employeeId,
        DateTime authoritativePunchTime)
    {
        if (employeeId <= 0)
            return;

        var features = await db.FeatureSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == 1);

        if (features?.EnableDualAttendance != true)
            return;

        await AcquireAttendanceAdvisoryLockAsync(
            db,
            employeeId);

        var windowStart =
            authoritativePunchTime.AddSeconds(
                -FallbackReconciliationWindowSeconds);

        var windowEnd =
            authoritativePunchTime.AddSeconds(
                FallbackReconciliationWindowSeconds);

        var fallback =
            await db.AttendanceLogs
                .Where(x =>
                    x.EmployeeID == employeeId &&
                    x.DeviceID == "GeofenceAuto" &&
                    x.BiometricID == "GEOFENCE_AUTO" &&
                    x.PunchTime >= windowStart &&
                    x.PunchTime <= windowEnd)
                .OrderBy(x =>
                    Math.Abs(
                        (x.PunchTime - authoritativePunchTime).TotalSeconds))
                .FirstOrDefaultAsync();

        if (fallback == null)
            return;

        db.AttendanceLogs.Remove(fallback);

        _logger.LogInformation(
            "Authoritative mobile punch replaced automatic geofence fallback. " +
            "EmployeeId={EmployeeId}, AuthoritativeTime={AuthoritativeTime}, " +
            "RemovedGeofenceLogId={GeofenceLogId}",
            employeeId,
            authoritativePunchTime,
            fallback.LogID);

        await db.SaveChangesAsync();
    }

    // ================================================================
    // SAVE PUNCH AUDIT
    // ================================================================

    private async Task SavePunchAuditAsync(
        AppDbContext db,
        int employeeId,
        Guid? sessionId,
        DateTime punchTimeUtc,
        double latitude,
        double longitude,
        double accuracyMeters,
        double distanceMeters,
        int allowedRadiusMeters,
        bool withinRadius,
        GeoPunchResult result,
        long? attendanceLogId = null,
        string source = "MOBILE_APP")
    {
        try
        {
            var audit = new GeoPunchAudit
            {
                EmployeeId = employeeId,
                SessionId = sessionId,
                PunchTimeUtc = punchTimeUtc,
                Latitude = latitude,
                Longitude = longitude,
                AccuracyMeters = accuracyMeters,
                DistanceFromOfficeMeters = distanceMeters,
                AllowedRadiusMeters = allowedRadiusMeters,
                IsWithinAllowedRadius = withinRadius,
                Success = result.Success,
                ResultMessage = result.Message,
                Source = source,
                AttendanceLogId = attendanceLogId
            };

            db.GeoPunchAudits.Add(audit);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            /*
             * Audit failure must NEVER break a valid punch.
             */
            _logger.LogError(
                ex,
                "Failed to save GPS punch audit for employee {EmployeeId}",
                employeeId);
        }
    }

    // ================================================================
    // NORMALIZE GPS ACCURACY
    // ================================================================

    private static double NormalizeAccuracy(
        double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value < 0)
        {
            return 0;
        }

        return value;
    }


    private static readonly TimeZoneInfo IndiaTimeZone =
    GetIndiaTimeZone();

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }
    }

    private static DateTime GetIndiaNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            IndiaTimeZone);
    }

    // ================================================================
    // NORMALIZE DISTANCE
    // ================================================================

    private static double NormalizeDistance(
        double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value < 0)
        {
            return 0;
        }

        return value;
    }

    // ================================================================
    // VALIDATE GPS COORDINATE
    // ================================================================

    private static bool IsValidCoordinate(
        double latitude,
        double longitude)
    {
        return
            !double.IsNaN(latitude) &&
            !double.IsNaN(longitude) &&
            !double.IsInfinity(latitude) &&
            !double.IsInfinity(longitude) &&
            latitude >= -90 &&
            latitude <= 90 &&
            longitude >= -180 &&
            longitude <= 180;
    }

    // ================================================================
    // HAVERSINE DISTANCE
    // ================================================================

    private static double CalculateDistance(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        const double earthRadiusMeters =
            6371e3;

        var rLat1 =
            lat1 *
            Math.PI /
            180;

        var rLat2 =
            lat2 *
            Math.PI /
            180;

        var dLat =
            (lat2 - lat1) *
            Math.PI /
            180;

        var dLon =
            (lon2 - lon1) *
            Math.PI /
            180;

        var a =
            Math.Sin(dLat / 2) *
            Math.Sin(dLat / 2) +
            Math.Cos(rLat1) *
            Math.Cos(rLat2) *
            Math.Sin(dLon / 2) *
            Math.Sin(dLon / 2);

        var c =
            2 *
            Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

        return
            earthRadiusMeters *
            c;
    }
}

// ====================================================================
// GEO PUNCH RESULT
// ====================================================================

public class GeoPunchResult
{
    public bool Success { get; set; }

    public string Message { get; set; } =
        string.Empty;
}

// ====================================================================
// GEO DISTANCE RESULT
// ====================================================================

public class GeoDistanceResult
{
    public bool Success { get; set; }

    public string Message { get; set; } =
        string.Empty;

    public double DistanceMeters { get; set; }

    public int AllowedRadiusMeters { get; set; }

    public bool IsWithinAllowedRadius =>
        Success &&
        DistanceMeters <= AllowedRadiusMeters;
}