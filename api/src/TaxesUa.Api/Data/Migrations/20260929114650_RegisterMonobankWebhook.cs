using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegisterMonobankWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WebhookFailedAt",
                table: "MonobankConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookFailure",
                table: "MonobankConnections",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "MonobankConnections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "MonobankConnections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoryImportedAt",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            // gen_random_uuid draws from the server's strong random source; two v4 uuids without their
            // dashes give the same 64 hex characters MonobankWebhooks.NewSecret does.
            migrationBuilder.Sql(
                """
                UPDATE "MonobankConnections"
                SET "WebhookSecret" = replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');
                """);

            // The rule this column replaces: a cursor within one window of now counted as imported.
            migrationBuilder.Sql(
                """
                UPDATE "BankAccounts"
                SET "HistoryImportedAt" = "SyncedThrough"
                WHERE "SyncedThrough" >= now() - interval '31 days';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MonobankConnections_WebhookSecret",
                table: "MonobankConnections",
                column: "WebhookSecret",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonobankConnections_WebhookSecret",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookFailedAt",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookFailure",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "HistoryImportedAt",
                table: "BankAccounts");
        }
    }
}
