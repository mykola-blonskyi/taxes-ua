using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <summary>
    /// Keeps only the SHA-256 of the calendar feed and monobank webhook path secrets (#256). Each stored
    /// secret is hashed in place, so the owner's calendar subscription and the webhook monobank holds keep
    /// answering. The hash columns are filled only where still empty, so the data step changes nothing on a
    /// second pass. Down cannot bring a plaintext secret back: see <see cref="Down"/>.
    /// </summary>
    public partial class HashPathSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecretHash",
                table: "CalendarFeeds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CalendarFeeds"
                SET "SecretHash" = encode(sha256(convert_to("Secret", 'UTF8')), 'hex')
                WHERE "SecretHash" IS NULL
                """);

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "CalendarFeeds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_CalendarFeeds_Secret",
                table: "CalendarFeeds");

            migrationBuilder.DropColumn(
                name: "Secret",
                table: "CalendarFeeds");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_SecretHash",
                table: "CalendarFeeds",
                column: "SecretHash",
                unique: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecretHash",
                table: "MonobankConnections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookBaseUrl",
                table: "MonobankConnections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // The registered URL is the base URL, the route and the secret. Only a URL registered for the
            // current secret keeps its base, so a pending or failed registration stays one.
            migrationBuilder.Sql("""
                UPDATE "MonobankConnections"
                SET "WebhookSecretHash" = encode(sha256(convert_to("WebhookSecret", 'UTF8')), 'hex'),
                    "WebhookBaseUrl" = CASE
                        WHEN right("WebhookUrl", length('/api/monobank/webhook/' || "WebhookSecret")) = '/api/monobank/webhook/' || "WebhookSecret"
                        THEN left("WebhookUrl", length("WebhookUrl") - length('/api/monobank/webhook/' || "WebhookSecret"))
                    END
                WHERE "WebhookSecretHash" IS NULL
                """);

            migrationBuilder.DropIndex(
                name: "IX_MonobankConnections_WebhookSecret",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "MonobankConnections");

            migrationBuilder.CreateIndex(
                name: "IX_MonobankConnections_WebhookSecretHash",
                table: "MonobankConnections",
                column: "WebhookSecretHash",
                unique: true);
        }

        /// <summary>
        /// Restores the schema, not the secrets: a hash cannot be reversed. Each feed's hash becomes its
        /// secret, so its old URL answers 404 and the owner rotates the link in settings. Each webhook's
        /// hash, or a fresh random value, becomes its secret with no registered URL, so the previous
        /// release's start registers that secret's URL with the bank again. The bank token that release
        /// starts with is the v2 ciphertext TokenUpgrade wrote, which it cannot read, so the owner
        /// connects monobank again.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Secret",
                table: "CalendarFeeds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""UPDATE "CalendarFeeds" SET "Secret" = "SecretHash" WHERE "Secret" IS NULL""");

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                table: "CalendarFeeds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_CalendarFeeds_SecretHash",
                table: "CalendarFeeds");

            migrationBuilder.DropColumn(
                name: "SecretHash",
                table: "CalendarFeeds");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_Secret",
                table: "CalendarFeeds",
                column: "Secret",
                unique: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "MonobankConnections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "MonobankConnections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "MonobankConnections"
                SET "WebhookSecret" = coalesce(
                    "WebhookSecretHash",
                    replace(gen_random_uuid()::text, '-', '') || replace(gen_random_uuid()::text, '-', ''))
                WHERE "WebhookSecret" IS NULL
                """);

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "MonobankConnections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_MonobankConnections_WebhookSecretHash",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookSecretHash",
                table: "MonobankConnections");

            migrationBuilder.DropColumn(
                name: "WebhookBaseUrl",
                table: "MonobankConnections");

            migrationBuilder.CreateIndex(
                name: "IX_MonobankConnections_WebhookSecret",
                table: "MonobankConnections",
                column: "WebhookSecret",
                unique: true);
        }
    }
}
