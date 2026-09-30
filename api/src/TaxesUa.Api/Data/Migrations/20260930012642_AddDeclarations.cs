using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeclarationDetails",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TaxOfficeRegion = table.Column<int>(type: "integer", nullable: true),
                    TaxOfficeDistrict = table.Column<int>(type: "integer", nullable: true),
                    KvedCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationDetails", x => x.UserId);
                    table.CheckConstraint("CK_DeclarationDetails_TaxOffice", "(\"TaxOfficeRegion\" IS NULL) = (\"TaxOfficeDistrict\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_DeclarationDetails_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeclarationFilings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Quarter = table.Column<int>(type: "integer", nullable: false),
                    FiledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FiledIncomeKop = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclarationFilings", x => new { x.UserId, x.Year, x.Quarter });
                    table.CheckConstraint("CK_DeclarationFilings_Quarter", "\"Quarter\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_DeclarationFilings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclarationDetails");

            migrationBuilder.DropTable(
                name: "DeclarationFilings");
        }
    }
}
