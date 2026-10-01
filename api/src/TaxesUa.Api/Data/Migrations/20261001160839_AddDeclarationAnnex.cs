using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarationAnnex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "AnnexContent",
                table: "DeclarationFiles",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnnexFileName",
                table: "DeclarationFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeclarationFiles_Annex",
                table: "DeclarationFiles",
                sql: "(\"AnnexFileName\" IS NULL) = (\"AnnexContent\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DeclarationFiles_Annex",
                table: "DeclarationFiles");

            migrationBuilder.DropColumn(
                name: "AnnexContent",
                table: "DeclarationFiles");

            migrationBuilder.DropColumn(
                name: "AnnexFileName",
                table: "DeclarationFiles");
        }
    }
}
