using Payroll.AttendanceService;
using Payroll.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

// 1. Create the Host Builder
IHost host = Host.CreateDefaultBuilder(args)
    // 2. Enable running as a Windows Service
    .UseWindowsService() 
    .ConfigureServices((hostContext, services) =>
    {
        // 3. Read Connection String from the Worker's appsettings.json
        var connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection");

        // 4. Register Database Context (Must match Web App's DB provider)
        // We use SetSwitch to handle Postgres timestamp behavior
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString), ServiceLifetime.Transient);

        // 5. Register the actual Worker Logic
        services.AddHostedService<Worker>();
    })
    .Build();

// 6. Run the Service
await host.RunAsync();