using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;

namespace Payroll.Web.Services;

public sealed class LocationHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public LocationHistoryService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<EmployeeLocationHistory>> GetTodayAsync(int employeeId)
    {
        if (employeeId <= 0)
            return new List<EmployeeLocationHistory>();

        var start = DateTime.UtcNow.Date;
        var end = start.AddDays(1);

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeLocationHistory
            .AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.RecordedAtUtc >= start &&
                x.RecordedAtUtc < end)
            .OrderBy(x => x.RecordedAtUtc)
            .ToListAsync();
    }

    public async Task<List<EmployeeLocationHistory>> GetSessionAsync(
        int employeeId,
        Guid sessionId)
    {
        if (employeeId <= 0 || sessionId == Guid.Empty)
            return new List<EmployeeLocationHistory>();

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeLocationHistory
            .AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.SessionId == sessionId)
            .OrderBy(x => x.RecordedAtUtc)
            .ToListAsync();
    }

    public async Task<List<EmployeeLocationHistory>> GetDateAsync(
        int employeeId,
        DateTime date)
    {
        if (employeeId <= 0)
            return new List<EmployeeLocationHistory>();

        var localDate =
            DateTime.SpecifyKind(
                date.Date,
                DateTimeKind.Unspecified);

        var indiaTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows()
                    ? "India Standard Time"
                    : "Asia/Kolkata");

        var start =
            TimeZoneInfo.ConvertTimeToUtc(
                localDate,
                indiaTimeZone);

        var end =
            TimeZoneInfo.ConvertTimeToUtc(
                localDate.AddDays(1),
                indiaTimeZone);

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeLocationHistory
            .AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.RecordedAtUtc >= start &&
                x.RecordedAtUtc < end)
            .OrderBy(x => x.RecordedAtUtc)
            .ToListAsync();
    }

    public async Task<EmployeeLocationHistory?> GetLatestAsync(
        int employeeId)
    {
        if (employeeId <= 0)
            return null;

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeLocationHistory
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<List<EmployeeGpsSession>> GetGpsSessionsAsync(
        int employeeId,
        DateTime date)
    {
        if (employeeId <= 0)
            return new List<EmployeeGpsSession>();

        var localDate =
            DateTime.SpecifyKind(
                date.Date,
                DateTimeKind.Local);

        var start =
            localDate.ToUniversalTime();

        var end =
            localDate.AddDays(1).ToUniversalTime();

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeGpsSessions
            .AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.StartedAtUtc < end &&
                (x.EndedAtUtc == null ||
                 x.EndedAtUtc >= start))
            .OrderByDescending(x => x.StartedAtUtc)
            .ToListAsync();
    }

    public async Task<EmployeeGpsSession?> GetGpsSessionAsync(
        int employeeId,
        Guid sessionId)
    {
        if (employeeId <= 0 ||
            sessionId == Guid.Empty)
        {
            return null;
        }

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeGpsSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.EmployeeId == employeeId &&
                x.SessionId == sessionId);
    }

    public async Task<EmployeeGpsSession?> GetLatestGpsSessionAsync(
        int employeeId)
    {
        if (employeeId <= 0)
            return null;

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        return await db.EmployeeGpsSessions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<EmployeeGpsSession?> GetActiveGpsSessionAsync(
        int employeeId)
    {
        if (employeeId <= 0)
            return null;

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

    public async Task<Dictionary<Guid, EmployeeGpsSession>>
        GetGpsSessionsByIdsAsync(
            int employeeId,
            IEnumerable<Guid> sessionIds)
    {
        if (employeeId <= 0)
            return new Dictionary<Guid, EmployeeGpsSession>();

        var ids =
            sessionIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, EmployeeGpsSession>();

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        var sessions =
            await db.EmployeeGpsSessions
                .AsNoTracking()
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    ids.Contains(x.SessionId))
                .ToListAsync();

        return sessions.ToDictionary(
            x => x.SessionId);
    }
}