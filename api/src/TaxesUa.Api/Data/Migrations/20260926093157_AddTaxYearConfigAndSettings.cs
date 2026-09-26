using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxYearConfigAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    FopRegistrationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentMode = table.Column<int>(type: "integer", nullable: false),
                    EsvRegistrationMonthPolicy = table.Column<int>(type: "integer", nullable: false),
                    EsvExempt = table.Column<bool>(type: "boolean", nullable: false),
                    TaxPaymentCountsFromStatutoryDeclarationDate = table.Column<bool>(type: "boolean", nullable: false),
                    ShiftTaxPaymentFromWeekend = table.Column<bool>(type: "boolean", nullable: false),
                    WeekendDays = table.Column<int[]>(type: "integer[]", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    Theme = table.Column<string>(type: "text", nullable: false),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Settings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxYearConfigs",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    MinWageKop = table.Column<long>(type: "bigint", nullable: false),
                    SingleTaxRateBp = table.Column<int>(type: "integer", nullable: false),
                    MilitaryLevyRateBp = table.Column<int>(type: "integer", nullable: false),
                    EsvRateBp = table.Column<int>(type: "integer", nullable: false),
                    ExcessRateBp = table.Column<int>(type: "integer", nullable: false),
                    EsvMonthlyKop = table.Column<long>(type: "bigint", nullable: false),
                    IncomeLimitMinWages = table.Column<int>(type: "integer", nullable: false),
                    IncomeLimitKop = table.Column<long>(type: "bigint", nullable: false),
                    LimitWarnThresholdsPct = table.Column<int[]>(type: "integer[]", nullable: false),
                    EsvDeadlineDay = table.Column<int>(type: "integer", nullable: false),
                    DeclarationDays = table.Column<int>(type: "integer", nullable: false),
                    TaxPaymentDaysAfterDeclaration = table.Column<int>(type: "integer", nullable: false),
                    AdvanceRecommendedDay = table.Column<int>(type: "integer", nullable: false),
                    Holidays = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxYearConfigs", x => x.Year);
                });

            migrationBuilder.InsertData(
                table: "TaxYearConfigs",
                columns: new[] { "Year", "AdvanceRecommendedDay", "DeclarationDays", "EsvDeadlineDay", "EsvMonthlyKop", "EsvRateBp", "ExcessRateBp", "Holidays", "IncomeLimitKop", "IncomeLimitMinWages", "LimitWarnThresholdsPct", "MilitaryLevyRateBp", "MinWageKop", "SingleTaxRateBp", "Source", "TaxPaymentDaysAfterDeclaration", "VerifiedAt" },
                values: new object[] { 2026, 15, 40, 19, 190234L, 2200, 1500, new DateOnly[0], 1009104900L, 1167, new[] { 85, 100 }, 100, 864700L, 500, "ЗУ «Про Держбюджет України на 2026 рік»; ПКУ ст. 293, 295, 296; ЗУ «Про ЄСВ» ст. 8", 10, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TaxYearConfigs");
        }
    }
}
