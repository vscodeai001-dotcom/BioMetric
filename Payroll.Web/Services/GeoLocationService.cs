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

        if (features?.EnableGeoFencing != true)
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

    public async Task UpdateGpsSessionAsync(
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
            return;
        }

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var session = await db.EmployeeGpsSessions
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.SessionId == sessionId);

            if (session == null)
            {
                /*
                 * A GPS update can arrive immediately after login.
                 * Create the session safely if it does not exist yet.
                 */
                var created = await StartGpsSessionAsync(
                    employeeId,
                    sessionId);

                if (!created)
                    return;

                session = await db.EmployeeGpsSessions
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == employeeId &&
                        x.SessionId == sessionId);

                if (session == null)
                    return;
            }

            /*
             * Never update a session which has already ended.
             */
            if (session.EndedAtUtc.HasValue)
                return;

            var safeAccuracy =
                NormalizeAccuracy(accuracyMeters);

            var safeDistance =
                NormalizeDistance(distanceMeters);

            var now = DateTime.UtcNow;

            session.LastUpdateAtUtc = now;
            session.LastLatitude = latitude;
            session.LastLongitude = longitude;
            session.LastAccuracyMeters = safeAccuracy;
            session.LastDistanceFromOfficeMeters = safeDistance;
            session.LastAllowedRadiusMeters =
                allowedRadiusMeters < 0
                    ? 0
                    : allowedRadiusMeters;
            session.LastIsWithinAllowedRadius =
                isWithinAllowedRadius;

            session.TotalPoints++;

            /*
             * This represents the accumulated distance supplied by the
             * GPS processing layer. We intentionally add the validated
             * distance value rather than inventing a movement distance.
             */
            session.TotalDistanceMeters += safeDistance;

            if (session.TotalPoints == 1)
            {
                session.AverageAccuracyMeters =
                    safeAccuracy;
            }
            else
            {
                var previousAverage =
                    session.AverageAccuracyMeters ?? 0;

                session.AverageAccuracyMeters =
                    ((previousAverage *
                      (session.TotalPoints - 1)) +
                     safeAccuracy) /
                    session.TotalPoints;
            }

            await db.SaveChangesAsync();

            /*
             * BROADCAST REAL-TIME LOCATION UPDATE
             *
             * Notify all connected admin clients of the location change
             * so the live map updates automatically without requiring refresh.
             */
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
                /*
                 * SignalR broadcast failure must never stop GPS tracking.
                 */
                _logger.LogWarning(
                    signalREx,
                    "Failed to broadcast location update via SignalR for employee {EmployeeId}",
                    employeeId);
            }
        }
        catch (Exception ex)
        {
            /*
             * Session database failure must never stop live GPS.
             */
            _logger.LogError(
                ex,
                "Failed to update GPS session. EmployeeId={EmployeeId}, SessionId={SessionId}",
                employeeId,
                sessionId);
        }
    }

    // ================================================================
    // END GPS SESSION
    // ================================================================

    public async Task EndGpsSessionAsync(
        int employeeId,
        Guid sessionId,
        string endReason = "LOGGED_OUT")
    {
        if (employeeId <= 0 ||
            sessionId == Guid.Empty)
        {
            return;
        }

        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var session = await db.EmployeeGpsSessions
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.SessionId == sessionId);

            if (session == null)
                return;

            if (session.EndedAtUtc.HasValue)
                return;

            session.EndedAtUtc = DateTime.UtcNow;

            session.EndReason =
                string.IsNullOrWhiteSpace(endReason)
                    ? "ENDED"
                    : endReason.Length > 40
                        ? endReason[..40]
                        : endReason;

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "GPS session ended. EmployeeId={EmployeeId}, SessionId={SessionId}, Reason={Reason}",
                employeeId,
                sessionId,
                session.EndReason);

            /*
             * BROADCAST SESSION END
             *
             * Notify all connected admin clients that a GPS session ended.
             */
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

    public async Task MarkTimedOutSessionsAsync()
    {
        try
        {
            await using var db =
                await _dbFactory.CreateDbContextAsync();

            var timeoutBefore =
                DateTime.UtcNow.AddSeconds(
                    -LiveLocationStore.StaleTimeoutSeconds);

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

        if (features?.EnableGeoFencing != true)
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

        try
        {
            await _refreshService
                .NotifyAttendanceChangedAsync(
                    employeeId,
                    DateOnly.FromDateTime(
                        log.PunchTime.Date));
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
        long? attendanceLogId = null)
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
                Source = "MOBILE_APP",
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