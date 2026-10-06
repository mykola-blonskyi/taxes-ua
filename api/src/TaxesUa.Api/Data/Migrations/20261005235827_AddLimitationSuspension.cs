using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLimitationSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LimitationSuspensionConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LimitationSuspensionConfigs", x => x.Id);
                    table.CheckConstraint("CK_LimitationSuspensionConfigs_EndAfterStart", "\"End\" IS NULL OR \"End\" >= \"Start\"");
                    table.CheckConstraint("CK_LimitationSuspensionConfigs_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "LimitationSuspensionConfigs",
                columns: new[] { "Id", "End", "Source", "Start" },
                values: new object[] { 1, null, "ПКУ п. 102.9 (ЗУ № 2120-IX, з 17.03.2022 по 31.07.2023), підп. 69.9 та 69.36 п. 69 підрозд. 10 розд. XX (ЗУ № 3219-IX, № 3453-IX)", new DateOnly(2022, 3, 17) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LimitationSuspensionConfigs");
        }
    }
}
