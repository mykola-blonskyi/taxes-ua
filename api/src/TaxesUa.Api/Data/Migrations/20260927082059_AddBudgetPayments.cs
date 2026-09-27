using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AmountKop = table.Column<long>(type: "bigint", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodQuarter = table.Column<int>(type: "integer", nullable: true),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetPayments", x => x.Id);
                    table.CheckConstraint("CK_BudgetPayments_AmountKop", "\"AmountKop\" > 0");
                    table.CheckConstraint("CK_BudgetPayments_OnePeriod", "(\"PeriodQuarter\" IS NULL) <> (\"PeriodMonth\" IS NULL)");
                    table.CheckConstraint("CK_BudgetPayments_PeriodMonth", "\"PeriodMonth\" IS NULL OR \"PeriodMonth\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_BudgetPayments_PeriodQuarter", "\"PeriodQuarter\" IS NULL OR \"PeriodQuarter\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_BudgetPayments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPayments_UserId_PeriodYear",
                table: "BudgetPayments",
                columns: new[] { "UserId", "PeriodYear" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetPayments");
        }
    }
}
