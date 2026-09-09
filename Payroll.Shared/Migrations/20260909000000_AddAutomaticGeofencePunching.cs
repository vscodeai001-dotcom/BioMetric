using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Shared.Migrations
{
    [Migration("20260909000000_AddAutomaticGeofencePunching")]
    public partial class AddAutomaticGeofencePunching : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE feature_settings " +
                "ADD COLUMN IF NOT EXISTS enable_automatic_geofence_punching boolean NOT NULL DEFAULT false;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE feature_settings " +
                "DROP COLUMN IF EXISTS enable_automatic_geofence_punching;");
        }
    }
}
