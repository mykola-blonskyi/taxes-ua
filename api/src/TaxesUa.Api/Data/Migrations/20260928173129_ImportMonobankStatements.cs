using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportMonobankStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BankTime",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Counterparty",
                table: "Transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "Transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportBatches_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BankAccountId_ExternalId",
                table: "Transactions",
                columns: new[] { "BankAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_BankAccountId_CreatedAt",
                table: "ImportBatches",
                columns: new[] { "BankAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UserId",
                table: "ImportBatches",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_BankAccounts_BankAccountId",
                table: "Transactions",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_BankAccounts_BankAccountId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_BankAccountId_ExternalId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankTime",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Counterparty",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "Transactions");
        }
    }
}
