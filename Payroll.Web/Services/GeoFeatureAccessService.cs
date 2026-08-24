using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;

namespace Payroll.Web.Services;

public sealed class GeoFeatureAccessService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AuthenticationStateProvider _authStateProvider;

    public GeoFeatureAccessService(
        IDbContextFactory<AppDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider)
    {
        _dbFactory = dbFactory;
        _authStateProvider = authStateProvider;
    }

    /// <summary>
    /// SuperAdmin always has access to Geo-Fencing.
    /// All other users follow FeatureSettings.EnableGeoFencing.
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        var authState =
            await _authStateProvider.GetAuthenticationStateAsync();

        var user = authState.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // ---------------------------------------------------------
        // SUPERADMIN OVERRIDE
        // ---------------------------------------------------------

        if (user.IsInRole("SuperAdmin"))
        {
            return true;
        }

        // ---------------------------------------------------------
        // NORMAL FEATURE TOGGLE
        // ---------------------------------------------------------

        await using var db =
            await _dbFactory.CreateDbContextAsync();

        var settings =
            await db.FeatureSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == 1);

        return settings?.EnableGeoFencing == true;
    }

    /// <summary>
    /// Returns true only when the authenticated user is SuperAdmin.
    /// </summary>
    public async Task<bool> IsSuperAdminAsync()
    {
        var authState =
            await _authStateProvider.GetAuthenticationStateAsync();

        return authState.User.IsInRole("SuperAdmin");
    }
}