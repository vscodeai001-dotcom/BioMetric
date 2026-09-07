using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Payroll.Web.Hubs;
using System.Runtime.CompilerServices;

namespace Payroll.Web.Services;

/// <summary>
/// Application-wide EF change publisher.
///
/// This is deliberately a notification layer only. It never changes entity
/// values, business rules, calculations, or transaction semantics.
/// After a successful SaveChanges operation containing user-facing data,
/// it broadcasts a lightweight invalidation event through the existing
/// SignalR hub. Clients then re-query their normal source of truth.
///
/// High-frequency GPS/session telemetry is excluded because it already has
/// its own realtime pipeline.
/// </summary>
public sealed class ApplicationDataChangeInterceptor : SaveChangesInterceptor
{
    private sealed class PendingChange
    {
        public bool Notify;
    }

    private readonly IHubContext<AttendanceRefreshHub> _hub;

    private readonly ConditionalWeakTable<DbContext, PendingChange> _pending = new();

    private static readonly HashSet<string> IgnoredEntityNames =
        new(StringComparer.Ordinal)
        {
            "EmployeeGpsSession",
            "EmployeeLocationHistory",
            "UserThemePreference",
            "Notification"
        };

    public ApplicationDataChangeInterceptor(
        IHubContext<AttendanceRefreshHub> hub)
    {
        _hub = hub;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        MarkIfUserFacingChange(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        MarkIfUserFacingChange(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishIfNeededAsync(eventData.Context, result, cancellationToken);
        return result;
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        PublishIfNeededAsync(eventData.Context, result, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
            _pending.Remove(eventData.Context);

        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(
        DbContextErrorEventData eventData)
    {
        if (eventData.Context != null)
            _pending.Remove(eventData.Context);
    }

    private void MarkIfUserFacingChange(DbContext? db)
    {
        if (db == null)
            return;

        var hasRelevantChange = db.ChangeTracker
            .Entries()
            .Any(e =>
                e.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted
                && !IgnoredEntityNames.Contains(e.Entity.GetType().Name));

        if (!hasRelevantChange)
            return;

        _pending.GetOrCreateValue(db).Notify = true;
    }

    private async Task PublishIfNeededAsync(
        DbContext? db,
        int result,
        CancellationToken cancellationToken)
    {
        if (db == null)
            return;

        var shouldNotify = false;

        if (_pending.TryGetValue(db, out var pending))
        {
            shouldNotify = pending.Notify;
            _pending.Remove(db);
        }

        if (!shouldNotify || result <= 0)
            return;

        try
        {
            await _hub.Clients.All.SendAsync(
                "DataChanged",
                new
                {
                    Scope = "APPLICATION",
                    Source = "EF_CORE",
                    Timestamp = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch
        {
            // Realtime notification failure must never fail an already
            // successful database transaction.
        }
    }
}
