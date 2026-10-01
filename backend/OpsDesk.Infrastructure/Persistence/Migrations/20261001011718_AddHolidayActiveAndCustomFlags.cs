using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayActiveAndCustomFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "holidays",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_custom",
                table: "holidays",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "holidays");

            migrationBuilder.DropColumn(
                name: "is_custom",
                table: "holidays");
        }
    }
}
