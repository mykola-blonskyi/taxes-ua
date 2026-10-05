using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DefaultLevyAccountEnd : Migration
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

            migrationBuilder.AddColumn<bool>(
                name: "LearnedEndRemoved",
                table: "TreasuryAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManualEndRemoved",
                table: "TreasuryAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MilitaryLevyAccountEnd",
                table: "TaxYearConfigs",
                type: "date",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TaxYearConfigs",
                keyColumn: "Year",
                keyValue: 2026,
                column: "MilitaryLevyAccountEnd",
                value: new DateOnly(2026, 12, 31));

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts",
                sql: "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL AND \"LearnedValidUntil\" IS NULL AND NOT \"LearnedEndRemoved\")) AND NOT (\"LearnedEndRemoved\" AND \"LearnedValidUntil\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts",
                sql: "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL) AND (\"ManualIban\" IS NOT NULL OR (\"ManualValidUntil\" IS NULL AND NOT \"ManualEndRemoved\")) AND NOT (\"ManualEndRemoved\" AND \"ManualValidUntil\" IS NOT NULL)");
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
                name: "LearnedEndRemoved",
                table: "TreasuryAccounts");

            migrationBuilder.DropColumn(
                name: "ManualEndRemoved",
                table: "TreasuryAccounts");

            migrationBuilder.DropColumn(
                name: "MilitaryLevyAccountEnd",
                table: "TaxYearConfigs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Learned",
                table: "TreasuryAccounts",
                sql: "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL AND \"LearnedValidUntil\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreasuryAccounts_Manual",
                table: "TreasuryAccounts",
                sql: "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL) AND (\"ManualIban\" IS NOT NULL OR \"ManualValidUntil\" IS NULL)");
        }
    }
}
