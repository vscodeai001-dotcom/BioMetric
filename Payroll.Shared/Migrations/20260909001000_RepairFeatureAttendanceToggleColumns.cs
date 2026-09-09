using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Shared.Migrations
{
    /// <summary>
    /// Defensive repair for deployments where the migration history says the
    /// feature-toggle columns were applied, but the physical columns are missing.
    /// This is safe to run repeatedly because both ALTER statements are idempotent.
    /// </summary>
    [Migration("20260909001000_RepairFeatureAttendanceToggleColumns")]
    public partial class RepairFeatureAttendanceToggleColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE public.feature_settings
                ADD COLUMN IF NOT EXISTS enable_dual_attendance boolean NOT NULL DEFAULT false;

                ALTER TABLE public.feature_settings
                ADD COLUMN IF NOT EXISTS enable_automatic_geofence_punching boolean NOT NULL DEFAULT false;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally do not remove these columns in the repair migration.
            // They belong to the application model and are managed by their
            // original migrations.
        }
    }
}
