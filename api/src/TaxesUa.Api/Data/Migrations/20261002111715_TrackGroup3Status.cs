using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackGroup3Status : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Group3ApplicationDays",
                table: "TaxYearConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "DpsAccountsRegistered",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DpsEsvRegistered",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DpsFopRegistered",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Group3ConfirmedOn",
                table: "Settings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Group3ReceiptNumber",
                table: "Settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Group3Since",
                table: "Settings",
                type: "date",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TaxYearConfigs",
                keyColumn: "Year",
                keyValue: 2026,
                column: "Group3ApplicationDays",
                value: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Group3ApplicationDays",
                table: "TaxYearConfigs");

            migrationBuilder.DropColumn(
                name: "DpsAccountsRegistered",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DpsEsvRegistered",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DpsFopRegistered",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Group3ConfirmedOn",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Group3ReceiptNumber",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Group3Since",
                table: "Settings");
        }
    }
}
