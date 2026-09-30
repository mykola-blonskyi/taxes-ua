using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmBudgetPaymentCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "BudgetPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "BudgetPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BudgetPaymentCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AmountKop = table.Column<long>(type: "bigint", nullable: false),
                    CounterIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    CounterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedKind = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetPaymentCandidates", x => x.Id);
                    table.CheckConstraint("CK_BudgetPaymentCandidates_AmountKop", "\"AmountKop\" > 0");
                    table.CheckConstraint("CK_BudgetPaymentCandidates_ConfirmedKind", "(\"Status\" = 1) = (\"ConfirmedKind\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BudgetPaymentCandidates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetPaymentCandidates_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPayments_BankAccountId_ExternalId",
                table: "BudgetPayments",
                columns: new[] { "BankAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetPayments_BankOperation",
                table: "BudgetPayments",
                sql: "(\"BankAccountId\" IS NULL) = (\"ExternalId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPaymentCandidates_BankAccountId_ExternalId",
                table: "BudgetPaymentCandidates",
                columns: new[] { "BankAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPaymentCandidates_UserId_Status",
                table: "BudgetPaymentCandidates",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetPayments_BankAccounts_BankAccountId",
                table: "BudgetPayments",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BudgetPayments_BankAccounts_BankAccountId",
                table: "BudgetPayments");

            migrationBuilder.DropTable(
                name: "BudgetPaymentCandidates");

            migrationBuilder.DropIndex(
                name: "IX_BudgetPayments_BankAccountId_ExternalId",
                table: "BudgetPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetPayments_BankOperation",
                table: "BudgetPayments");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "BudgetPayments");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "BudgetPayments");
        }
    }
}
