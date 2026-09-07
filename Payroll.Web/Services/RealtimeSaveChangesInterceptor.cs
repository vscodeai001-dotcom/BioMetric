using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Payroll.Web.Services;

/// <summary>
/// Publishes one application-wide realtime notification after a successful
/// EF Core write. This is deliberately a notification-only layer:
/// database state and all existing business logic remain authoritative.
/// High-frequency GPS/session persistence is excluded because those paths
/// already have their own live-location SignalR channel.
/// </summary>
public sealed class RealtimeSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly AttendanceRefreshService _refreshService;

    private readonly ConcurrentDictionary<Guid, string[]> _pendingChanges = new();

    private static readonly HashSet<string> IgnoredHighFrequencyEntities =
        new(StringComparer.Ordinal)
        {
            "EmployeeGpsSession",
            "EmployeeLocationHistory",
            "Notification",
            "UserThemePreference"
        };

    public RealtimeSaveChangesInterceptor(
        AttendanceRefreshService refreshService)
    {
        _refreshService = refreshService;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CaptureChanges(eventData.Context);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureChanges(eventData.Context);
        return await new ValueTask<InterceptionResult<int>>(result);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        PublishChanges(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishChangesAsync(eventData.Context);
        return result;
    }

    public override void SaveChangesFailed(
        DbContextErrorEventData eventData)
    {
        RemovePending(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RemovePending(eventData.Context);
        return Task.CompletedTask;
    }

    private void CaptureChanges(DbContext? dbContext)
    {
        if (dbContext == null)
            return;

        var changedEntities = dbContext.ChangeTracker
            .Entries()
            .Where(e =>
                e.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted)
            .Select(e => e.Metadata.ClrType.Name)
            .Where(name => !IgnoredHighFrequencyEntities.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (changedEntities.Length == 0)
        {
            _pendingChanges.TryRemove(
                dbContext.ContextId.InstanceId,
                out _);

            return;
        }

        _pendingChanges[
            dbContext.ContextId.InstanceId] = changedEntities;
    }

    private void PublishChanges(DbContext? dbContext)
    {
        if (dbContext == null)
            return;

        if (!_pendingChanges.TryRemove(
                dbContext.ContextId.InstanceId,
                out var changedEntities))
        {
            return;
        }

        _ = PublishAsync(changedEntities);
    }

    private async ValueTask PublishChangesAsync(DbContext? dbContext)
    {
        if (dbContext == null)
            return;

        if (!_pendingChanges.TryRemove(
                dbContext.ContextId.InstanceId,
                out var changedEntities))
        {
            return;
        }

        await PublishAsync(changedEntities);
    }

    private async Task PublishAsync(string[] changedEntities)
    {
        try
        {
            await _refreshService.NotifyApplicationDataChangedAsync(
                changedEntities);
        }
        catch
        {
            // Realtime notification must never make a successful database
            // transaction fail. The database operation remains authoritative.
        }
    }

    private void RemovePending(DbContext? dbContext)
    {
        if (dbContext == null)
            return;

        _pendingChanges.TryRemove(
            dbContext.ContextId.InstanceId,
            out _);
    }
}
