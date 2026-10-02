using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentToSentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SentReminders_UserId_Date_Kinds_Offset_Channel",
                table: "SentReminders");

            migrationBuilder.AddColumn<string>(
                name: "Incident",
                table: "SentReminders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SentReminders_UserId_Date_Kinds_Offset_Channel",
                table: "SentReminders",
                columns: new[] { "UserId", "Date", "Kinds", "Offset", "Channel" },
                unique: true,
                filter: "\"Incident\" = ''");

            migrationBuilder.CreateIndex(
                name: "IX_SentReminders_UserId_Incident_Channel",
                table: "SentReminders",
                columns: new[] { "UserId", "Incident", "Channel" },
                unique: true,
                filter: "\"Incident\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Incident rows have no meaning without the column, and would collide on the old unique index.
            migrationBuilder.Sql("DELETE FROM \"SentReminders\" WHERE \"Incident\" <> '';");

            migrationBuilder.DropIndex(
                name: "IX_SentReminders_UserId_Date_Kinds_Offset_Channel",
                table: "SentReminders");

            migrationBuilder.DropIndex(
                name: "IX_SentReminders_UserId_Incident_Channel",
                table: "SentReminders");

            migrationBuilder.DropColumn(
                name: "Incident",
                table: "SentReminders");

            migrationBuilder.CreateIndex(
                name: "IX_SentReminders_UserId_Date_Kinds_Offset_Channel",
                table: "SentReminders",
                columns: new[] { "UserId", "Date", "Kinds", "Offset", "Channel" },
                unique: true);
        }
    }
}
