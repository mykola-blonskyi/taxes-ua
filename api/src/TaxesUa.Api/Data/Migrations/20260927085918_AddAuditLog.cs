using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Entity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.CheckConstraint("CK_AuditLog_Snapshots", "(\"Action\" = 'Create' AND \"Before\" IS NULL AND \"After\" IS NOT NULL) OR (\"Action\" = 'Update' AND \"Before\" IS NOT NULL AND \"After\" IS NOT NULL) OR (\"Action\" = 'Delete' AND \"Before\" IS NOT NULL AND \"After\" IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId_At",
                table: "AuditLog",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId_Entity_EntityId_At",
                table: "AuditLog",
                columns: new[] { "UserId", "Entity", "EntityId", "At" });

            // Append-only against every code path, not only the api's: an entry that could be edited
            // would not be a record of what happened. AuditLogTests fails if a regenerated migration
            // drops this.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_log_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'AuditLog is append-only';
                END;
                $$;

                CREATE TRIGGER audit_log_append_only
                    BEFORE UPDATE OR DELETE ON "AuditLog"
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.Sql("DROP FUNCTION audit_log_append_only();");
        }
    }
}
