using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Shared.Migrations;

[Migration("20260830000000_AddUserThemePreferences")]
public partial class AddUserThemePreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_theme_preferences",
            schema: "public",
            columns: table => new
            {
                user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                theme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_theme_preferences", x => x.user_id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_theme_preferences",
            schema: "public");
    }
}