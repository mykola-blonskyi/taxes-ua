using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTreasuryAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterEdrpou",
                table: "BudgetPaymentCandidates",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TreasuryAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ManualIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    ManualRecipientName = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    ManualRecipientCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ManualUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LearnedIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    LearnedRecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LearnedRecipientCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    LearnedExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LearnedPaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LearnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NoticeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryAccounts", x => x.Id);
                    table.CheckConstraint("CK_TreasuryAccounts_Learned", "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL))");
                    table.CheckConstraint("CK_TreasuryAccounts_Manual", "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL)");
                    table.CheckConstraint("CK_TreasuryAccounts_Notice", "\"NoticeAt\" IS NULL OR (\"ManualIban\" IS NOT NULL AND \"LearnedIban\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TreasuryAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryAccounts_UserId_Kind",
                table: "TreasuryAccounts",
                columns: new[] { "UserId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreasuryAccounts");

            migrationBuilder.DropColumn(
                name: "CounterEdrpou",
                table: "BudgetPaymentCandidates");
        }
    }
}
