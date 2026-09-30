using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackOnGroup3AfterLimitCrossing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackOnGroup3FromQuarter",
                table: "Settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackOnGroup3FromYear",
                table: "Settings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackOnGroup3FromQuarter",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BackOnGroup3FromYear",
                table: "Settings");
        }
    }
}
