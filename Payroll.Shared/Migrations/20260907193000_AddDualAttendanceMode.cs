using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Shared.Migrations
{
    [Migration("20260907193000_AddDualAttendanceMode")]
    public partial class AddDualAttendanceMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent for the current Neon deployment, where the column
            // may already have been provisioned manually before this migration
            // is recorded in __EFMigrationsHistory.
            migrationBuilder.Sql(
                "ALTER TABLE feature_settings " +
                "ADD COLUMN IF NOT EXISTS enable_dual_attendance boolean NOT NULL DEFAULT false;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enable_dual_attendance",
                table: "feature_settings");
        }
    }
}
