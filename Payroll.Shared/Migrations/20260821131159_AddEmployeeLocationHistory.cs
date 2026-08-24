using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payroll.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLocationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_location_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    DistanceFromOfficeMeters = table.Column<double>(type: "double precision", nullable: false),
                    AllowedRadiusMeters = table.Column<int>(type: "integer", nullable: false),
                    IsWithinAllowedRadius = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_location_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_location_history_EmployeeId_RecordedAtUtc",
                table: "employee_location_history",
                columns: new[] { "EmployeeId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_location_history_SessionId_RecordedAtUtc",
                table: "employee_location_history",
                columns: new[] { "SessionId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_location_history");
        }
    }
}
