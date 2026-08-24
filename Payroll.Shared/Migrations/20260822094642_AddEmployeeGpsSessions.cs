using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payroll.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeGpsSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_gps_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_update_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    last_distance_from_office_meters = table.Column<double>(type: "double precision", nullable: true),
                    last_allowed_radius_meters = table.Column<int>(type: "integer", nullable: true),
                    last_is_within_allowed_radius = table.Column<bool>(type: "boolean", nullable: true),
                    total_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_distance_meters = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    average_accuracy_meters = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_gps_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_gps_sessions_employee_id_ended_at_utc",
                table: "employee_gps_sessions",
                columns: new[] { "employee_id", "ended_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_gps_sessions_employee_id_started_at_utc",
                table: "employee_gps_sessions",
                columns: new[] { "employee_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_gps_sessions_session_id",
                table: "employee_gps_sessions",
                column: "session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_gps_sessions");
        }
    }
}
