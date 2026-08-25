using Blazored.Toast;
using Blazored.Toast.Services;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Payroll.Shared;
using Payroll.Shared.Data;
using Payroll.Shared.Services;
using Payroll.Web.Components;
using Payroll.Web.Hubs;
using Payroll.Web.Services;

// ============================================================
// APPLICATION OPTIONS
// ============================================================

var options = new WebApplicationOptions
{
    Args = args,

    ContentRootPath =
        WindowsServiceHelpers.IsWindowsService()
            ? AppContext.BaseDirectory
            : default
};

// ============================================================
// BUILDER
// ============================================================
Environment.SetEnvironmentVariable(
    "DOTNET_USE_POLLING_FILE_WATCHER",
    "true");
var builder = WebApplication.CreateBuilder(options);

// ============================================================
// WINDOWS SERVICE
// ============================================================

builder.Host.UseWindowsService();

// ============================================================
// SIGNALR
// ============================================================

builder.Services.AddSignalR();

builder.Services.AddSingleton<AttendanceRefreshService>();

// ============================================================
// POSTGRESQL DATETIME COMPATIBILITY
// ============================================================

AppContext.SetSwitch(
    "Npgsql.EnableLegacyTimestampBehavior",
    true);

// ============================================================
// DATABASE
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "DefaultConnection is not configured.");
}

builder.Services.AddDbContextFactory<AppDbContext>(
    options =>
        options.UseNpgsql(connectionString));

// ============================================================
// ATTENDANCE ENGINE REGISTRATIONS
// ============================================================

builder.Services.AddScoped<AttendanceCalculatorService>();
builder.Services.AddScoped<AttendanceBoundsService>();

// ============================================================
// APPLICATION SERVICES
// ============================================================

builder.Services.AddScoped<AttendancePunchProcessor>();
builder.Services.AddScoped<AttendanceScheduleService>();
builder.Services.AddScoped<AttendanceBreakPenaltyService>();
builder.Services.AddScoped<AttendanceDayTypeService>();
builder.Services.AddScoped<AttendanceOvertimeService>();
builder.Services.AddScoped<DailySummaryBuilder>();
builder.Services.AddScoped<AttendanceLeavePostingService>();
builder.Services.AddScoped<SalaryStructureService>();
builder.Services.AddScoped<LeaveAccrualService>();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<RosteringService>();
builder.Services.AddScoped<BankExportService>();
builder.Services.AddScoped<DashboardAnalyticsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<GeoLocationService>();
builder.Services.AddScoped<GeoFeatureAccessService>();
builder.Services.AddScoped<ResignationService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<RegularizationService>();
builder.Services.AddScoped<PayrollProcessorService>();
builder.Services.AddScoped<LocationHistoryService>();
builder.Services.AddScoped<LeaveManagementService>();

// ============================================================
// OTHER SERVICES
// ============================================================

builder.Services.AddTransient<AutomatedJobsService>();

builder.Services.AddScoped<NotificationService>();

builder.Services.AddScoped<IEmailSender, EmailSender>();

builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<FBPService>();
builder.Services.AddScoped<PayrollLockService>();

builder.Services.AddTransient<YearEndSummaryService>();

builder.Services.AddScoped<TaxDeclarationService>();
builder.Services.AddScoped<FeatureCleanUpService>();
builder.Services.AddScoped<EmployeeDeletionService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery();

// ============================================================
// HTTP CLIENT
// ============================================================

builder.Services.AddScoped(sp =>
{
    var nav =
        sp.GetRequiredService<NavigationManager>();

    return new HttpClient
    {
        BaseAddress =
            new Uri(nav.BaseUri)
    };
});

// ============================================================
// IDENTITY + ROLES
// ============================================================

builder.Services.AddIdentity<IdentityUser, IdentityRole>(
    options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultUI()
    .AddTokenProvider<EmailTokenProvider<IdentityUser>>(
        "Default")
    .AddDefaultTokenProviders();

// ============================================================
// AUTHORIZATION POLICIES
// ============================================================

builder.Services.AddAuthorizationBuilder()

    .AddPolicy(
        "EmployeeOnly",
        p => p.RequireRole("Employee"))

    .AddPolicy(
        "AdminOnly",
        p => p.RequireRole("Admin"))

    .AddPolicy(
        "SuperOnly",
        p => p.RequireRole("SuperAdmin"))

    .AddPolicy(
        "AdminOrSuper",
        p => p.RequireRole(
            "Admin",
            "SuperAdmin"))

    .AddPolicy(
        "EmployeeOrHigher",
        p => p.RequireRole(
            "Employee",
            "Admin",
            "SuperAdmin"));

// ============================================================
// RAZOR PAGES
// ============================================================

builder.Services.AddRazorPages();

builder.Services.AddSingleton<
    IActionContextAccessor,
    ActionContextAccessor>();

// ============================================================
// BLAZOR
// ============================================================

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddBlazoredToast();

// ============================================================
// HANGFIRE
// ============================================================

builder.Services.AddHangfire(
    (serviceProvider, config) =>
    {
        config
            .SetDataCompatibilityLevel(
                CompatibilityLevel.Version_170)

            .UseSimpleAssemblyNameTypeSerializer()

            .UseRecommendedSerializerSettings()

            .UsePostgreSqlStorage(
                options =>
                {
                    options.UseNpgsqlConnection(
                        connectionString);
                },
                new PostgreSqlStorageOptions
                {
                    QueuePollInterval =
                        TimeSpan.FromSeconds(15),

                    SchemaName = "hangfire"
                });
    });

// ============================================================
// HANGFIRE SERVER
// ============================================================

builder.Services.AddHangfireServer(
    options =>
    {
        options.WorkerCount =
            Math.Max(
                1,
                Environment.ProcessorCount * 2);
    });

// ============================================================
// BUILD APPLICATION
// ============================================================

var app = builder.Build();

// ============================================================
// SIGNALR HUB
// ============================================================

app.MapHub<AttendanceRefreshHub>(
    "/hubs/attendance-refresh");

// ============================================================
// DATABASE MIGRATION
// ============================================================

try
{
    using var scope =
        app.Services.CreateScope();

    var db =
        scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}
catch (Exception ex)
{
    var logger =
        app.Services
            .GetRequiredService<
                ILogger<Program>>();

    logger.LogError(
        ex,
        "Error during DB migration.");
}

// ============================================================
// INITIAL SEEDING
// ============================================================

try
{
    using var scope =
        app.Services.CreateScope();

    await SeedRolesAsync(
        scope.ServiceProvider);

    await SeedCompanySettingsAsync(
        scope.ServiceProvider);

    await SeedAdminUserAsync(
        scope.ServiceProvider);

    await EnsureEmployeeRoleForAllUsers(
        scope.ServiceProvider);
}
catch (Exception ex)
{
    var logger =
        app.Services
            .GetRequiredService<
                ILogger<Program>>();

    logger.LogError(
        ex,
        "Error during initial seeding.");
}

// ============================================================
// ERROR HANDLING
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

// ============================================================
// MIDDLEWARE PIPELINE
// ============================================================

//app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

// ============================================================
// CONTROLLERS
// ============================================================

app.MapControllers();

app.UseAntiforgery();

// ============================================================
// HANGFIRE
// ============================================================
//
// IMPORTANT:
// Do NOT use the static JobStorage.Current /
// RecurringJob.AddOrUpdate APIs here.
//
// Resolve Hangfire services from DI.
// ============================================================

var hangfireStorage =
    app.Services.GetRequiredService<JobStorage>();

var recurringJobManager =
    app.Services
        .GetRequiredService<IRecurringJobManager>();

app.UseHangfireDashboard(
    "/hangfire",
    new DashboardOptions
    {
        Authorization =
        [
            new HangfireAuth()
        ]
    },
    hangfireStorage);

// ============================================================
// RECURRING JOBS
// ============================================================

recurringJobManager.AddOrUpdate<AutomatedJobsService>(
    "mark-daily-absences",
    s => s.MarkYesterdayAbsencesAsync(),
    "5 9 * * *",
    new RecurringJobOptions
    {
        TimeZone =
            TimeZoneInfo.Local
    });

recurringJobManager.AddOrUpdate<LeaveAccrualService>(
    "monthly-leave-accrual",
    s => s.RunMonthlyAccrualAsync(),
    "0 0 1 * *",
    new RecurringJobOptions
    {
        TimeZone =
            TimeZoneInfo.Local
    });

recurringJobManager.AddOrUpdate<YearEndSummaryService>(
    "annual-yearend-summary",
    s => s.RunYearEndConsolidationAsync(
        DateTime.Now.Year - 1),
    "0 1 1 1 *",
    new RecurringJobOptions
    {
        TimeZone =
            TimeZoneInfo.Local
    });

recurringJobManager.AddOrUpdate<RosteringService>(
    "monthly-roster-generation",
    s =>
        s.GenerateScheduleFromPatternsAsync(
            DateOnly.FromDateTime(
                DateTime.Now.Date),
            DateOnly.FromDateTime(
                DateTime.Now.Date.AddDays(30))),
    "15 0 1 * *",
    new RecurringJobOptions
    {
        TimeZone =
            TimeZoneInfo.Local
    });

recurringJobManager.AddOrUpdate<RosteringService>(
    "weekly-shift-rotation",
    s => s.RunShiftRotationJobAsync(),
    "0 2 * * 0",
    new RecurringJobOptions
    {
        TimeZone =
            TimeZoneInfo.Local
    });

// ============================================================
// RAZOR COMPONENTS
// ============================================================

app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ============================================================
// RUN
// ============================================================

app.Run();

// ============================================================
// HELPERS
// ============================================================

async Task SeedRolesAsync(
    IServiceProvider sp)
{
    var roleMgr =
        sp.GetRequiredService<
            RoleManager<IdentityRole>>();

    string[] roles =
    [
        "SuperAdmin",
        "Admin",
        "Employee"
    ];

    foreach (var role in roles)
    {
        if (!await roleMgr.RoleExistsAsync(role))
        {
            await roleMgr.CreateAsync(
                new IdentityRole(role));
        }
    }
}

// ============================================================

async Task SeedCompanySettingsAsync(
    IServiceProvider sp)
{
    var db =
        sp.GetRequiredService<AppDbContext>();

    if (!await db.CompanySettings
        .AnyAsync(x => x.SettingID == 1))
    {
        db.CompanySettings.Add(
            new CompanySetting
            {
                SettingID = 1,
                CompanyName = "Your Company Name",
                LateGraceMinutes = 5,
                SalaryCalculationMethod =
                    "Days in Month",
                ZktecoIP =
                    "192.168.1.201",
                ZktecoPort = 4370,
                ZktecoMachineNumber = 1
            });

        await db.SaveChangesAsync();
    }

    if (!await db.FeatureSettings
        .AnyAsync(x => x.Id == 1))
    {
        db.FeatureSettings.Add(
            new FeatureSettings
            {
                Id = 1
            });

        await db.SaveChangesAsync();
    }
}

// ============================================================

async Task SeedAdminUserAsync(
    IServiceProvider sp)
{
    var userMgr =
        sp.GetRequiredService<
            UserManager<IdentityUser>>();

    var roleMgr =
        sp.GetRequiredService<
            RoleManager<IdentityRole>>();

    if (!await roleMgr.RoleExistsAsync(
            "SuperAdmin"))
    {
        return;
    }

    var superAdmins =
        await userMgr.GetUsersInRoleAsync(
            "SuperAdmin");

    if (superAdmins.Any())
    {
        return;
    }

    var firstUser =
        await userMgr.Users
            .OrderBy(u => u.UserName)
            .FirstOrDefaultAsync();

    if (firstUser != null)
    {
        await userMgr.AddToRoleAsync(
            firstUser,
            "SuperAdmin");
    }
}

// ============================================================

async Task EnsureEmployeeRoleForAllUsers(
    IServiceProvider sp)
{
    var userMgr =
        sp.GetRequiredService<
            UserManager<IdentityUser>>();

    var roleMgr =
        sp.GetRequiredService<
            RoleManager<IdentityRole>>();

    if (!await roleMgr.RoleExistsAsync(
            "Employee"))
    {
        return;
    }

    var users =
        await userMgr.Users
            .ToListAsync();

    foreach (var user in users)
    {
        var roles =
            await userMgr.GetRolesAsync(user);

        if (!roles.Any())
        {
            await userMgr.AddToRoleAsync(
                user,
                "Employee");
        }
    }
}

// ============================================================
// HANGFIRE AUTHORIZATION
// ============================================================

public class HangfireAuth
    : IDashboardAuthorizationFilter
{
    public bool Authorize(
        DashboardContext context)
    {
        var httpContext =
            context.GetHttpContext();

        if (httpContext == null ||
            httpContext.User == null)
        {
            return false;
        }

        var user =
            httpContext.User;

        return
            user.Identity?.IsAuthenticated == true &&
            (
                user.IsInRole("Admin") ||
                user.IsInRole("SuperAdmin")
            );
    }
}