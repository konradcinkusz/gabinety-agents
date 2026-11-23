using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferingKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "offerings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Tasks");

            migrationBuilder.AddColumn<int>(
                name: "units",
                table: "offerings",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "offerings");

            migrationBuilder.DropColumn(
                name: "units",
                table: "offerings");
        }
    }
}
