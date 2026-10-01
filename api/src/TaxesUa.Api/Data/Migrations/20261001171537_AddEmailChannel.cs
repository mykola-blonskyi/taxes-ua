using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "NotificationChannels",
                type: "timestamp with time zone",
                nullable: true);

            // Every channel until now is a Telegram chat, confirmed by pressing Start when it was linked.
            migrationBuilder.Sql("UPDATE \"NotificationChannels\" SET \"ConfirmedAt\" = \"LinkedAt\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "NotificationChannels");
        }
    }
}
