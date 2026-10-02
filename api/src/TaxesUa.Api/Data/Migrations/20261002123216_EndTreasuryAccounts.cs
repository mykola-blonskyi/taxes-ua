using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EndTreasuryAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LearnedValidUntil",
                table: "TreasuryAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ManualValidUntil",
                table: "TreasuryAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts",
                sql: "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL AND \"LearnedValidUntil\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts",
                sql: "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL) AND (\"ManualIban\" IS NOT NULL OR \"ManualValidUntil\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts");

            migrationBuilder.DropColumn(
                name: "LearnedValidUntil",
                table: "TreasuryAccounts");

            migrationBuilder.DropColumn(
                name: "ManualValidUntil",
                table: "TreasuryAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts",
                sql: "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts",
                sql: "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL)");
        }
    }
}
