using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RateDate",
                table: "Transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateSource",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RateE4 = table.Column<int>(type: "integer", nullable: false),
                    RateDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => new { x.Currency, x.Date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropColumn(
                name: "RateDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RateSource",
                table: "Transactions");
        }
    }
}
