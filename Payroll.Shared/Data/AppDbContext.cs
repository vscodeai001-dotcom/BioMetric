using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.Shared.Data;

public class AppDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees { get; set; }

    public DbSet<AttendanceLog> AttendanceLogs { get; set; }

    public DbSet<SalaryAdvance> SalaryAdvances { get; set; }

    public DbSet<PayrollHistory> PayrollHistories { get; set; }

    public DbSet<LeaveRequest> LeaveRequests { get; set; }

    public DbSet<ShiftSchedule> ShiftSchedules { get; set; }

    public DbSet<CompanyHoliday> CompanyHolidays { get; set; }

    public DbSet<CompanySetting> CompanySettings { get; set; }

    public DbSet<DailySummary> DailySummaries { get; set; }

    public DbSet<FeatureSettings> FeatureSettings { get; set; }

    public DbSet<ProfessionalTaxSlab> ProfessionalTaxSlabs { get; set; }

    public DbSet<AuditLog> AuditLogs { get; set; }

    public DbSet<BonusRecord> BonusRecords { get; set; }

    public DbSet<YearEndSummary> YearEndSummaries { get; set; }

    public DbSet<TaxDeclaration> TaxDeclarations { get; set; }

    public DbSet<ResignationRequest> ResignationRequests { get; set; }

    public DbSet<FnFSettlement> FnFSettlements { get; set; }

    public DbSet<Notification> Notifications { get; set; }

    public DbSet<ReportDefinition> ReportDefinitions { get; set; }

    public DbSet<AttendanceRegularization> AttendanceRegularizations { get; set; }

    public DbSet<FBPComponent> FBPComponents { get; set; }

    public DbSet<GeoPunchAudit> GeoPunchAudits { get; set; }

    public DbSet<FlexibleBenefitDeclaration> FlexibleBenefitDeclarations { get; set; }
    public DbSet<EmployeeGpsSession> EmployeeGpsSessions { get; set; }
    public DbSet<EmployeeLocationHistory> EmployeeLocationHistory { get; set; }


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);


        // ============================================================
        // ASP.NET IDENTITY KEYS
        // ============================================================

        builder.Entity<IdentityUser>()
            .HasKey(u => u.Id);

        builder.Entity<IdentityRole>()
            .HasKey(r => r.Id);

        builder.Entity<IdentityRoleClaim<string>>()
            .HasKey(rc => rc.Id);

        builder.Entity<IdentityUserClaim<string>>()
            .HasKey(uc => uc.Id);

        builder.Entity<IdentityUserToken<string>>()
            .HasKey(ut => new
            {
                ut.UserId,
                ut.LoginProvider,
                ut.Name
            });

        builder.Entity<IdentityUserLogin<string>>()
            .HasKey(ul => new
            {
                ul.LoginProvider,
                ul.ProviderKey
            });

        builder.Entity<IdentityUserRole<string>>()
            .HasKey(ur => new
            {
                ur.UserId,
                ur.RoleId
            });


        // ============================================================
        // EMPLOYEE GPS LOCATION HISTORY
        // ============================================================

        builder.Entity<EmployeeLocationHistory>(entity =>
        {
            entity.ToTable("employee_location_history");


            // --------------------------------------------------------
            // PRIMARY KEY
            // --------------------------------------------------------

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedOnAdd();


            // --------------------------------------------------------
            // EMPLOYEE
            // --------------------------------------------------------

            entity.Property(x => x.EmployeeId)
                .IsRequired();


            // --------------------------------------------------------
            // GPS SESSION
            // --------------------------------------------------------

            entity.Property(x => x.SessionId)
                .IsRequired();


            // --------------------------------------------------------
            // GPS COORDINATES
            // --------------------------------------------------------

            entity.Property(x => x.Latitude)
                .IsRequired();

            entity.Property(x => x.Longitude)
                .IsRequired();


            // --------------------------------------------------------
            // GPS ACCURACY
            // --------------------------------------------------------

            entity.Property(x => x.AccuracyMeters)
                .HasColumnName("accuracy_meters")
                .IsRequired();


            // --------------------------------------------------------
            // OFFICE DISTANCE
            // --------------------------------------------------------

            entity.Property(x => x.DistanceFromOfficeMeters)
                .IsRequired();


            // --------------------------------------------------------
            // GEO-FENCE RADIUS
            // --------------------------------------------------------

            entity.Property(x => x.AllowedRadiusMeters)
                .IsRequired();


            // --------------------------------------------------------
            // GEO-FENCE RESULT
            // --------------------------------------------------------

            entity.Property(x => x.IsWithinAllowedRadius)
                .IsRequired();


            // --------------------------------------------------------
            // TIMESTAMP
            // --------------------------------------------------------

            entity.Property(x => x.RecordedAtUtc)
                .IsRequired();


            // --------------------------------------------------------
            // PERFORMANCE INDEX
            //
            // Used for:
            // Employee + date/time history queries
            // --------------------------------------------------------

            entity.HasIndex(x => new
            {
                x.EmployeeId,
                x.RecordedAtUtc
            });


            // --------------------------------------------------------
            // SESSION INDEX
            //
            // Used for:
            // Route playback / individual GPS sessions
            // --------------------------------------------------------

            entity.HasIndex(x => new
            {
                x.SessionId,
                x.RecordedAtUtc
            });
        });

        builder.Entity<EmployeeGpsSession>(entity =>
        {
            entity.ToTable("employee_gps_sessions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedOnAdd();

            entity.Property(x => x.EmployeeId)
                .IsRequired();

            entity.Property(x => x.SessionId)
                .IsRequired();

            entity.Property(x => x.StartedAtUtc)
                .IsRequired();

            entity.Property(x => x.LastUpdateAtUtc)
                .IsRequired();

            entity.Property(x => x.EndReason)
                .HasMaxLength(40);

            entity.Property(x => x.TotalPoints)
                .HasDefaultValue(0);

            entity.Property(x => x.TotalDistanceMeters)
                .HasDefaultValue(0d);

            entity.HasIndex(x => x.SessionId)
                .IsUnique();

            entity.HasIndex(x => new
            {
                x.EmployeeId,
                x.StartedAtUtc
            });

            entity.HasIndex(x => new
            {
                x.EmployeeId,
                x.EndedAtUtc
            });
        });
    }


}