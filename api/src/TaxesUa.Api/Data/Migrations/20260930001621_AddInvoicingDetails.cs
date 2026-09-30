using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoicingDetails",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SellerNameUk = table.Column<string>(type: "text", nullable: false),
                    SellerNameEn = table.Column<string>(type: "text", nullable: false),
                    Rnokpp = table.Column<string>(type: "text", nullable: false),
                    AddressUk = table.Column<string>(type: "text", nullable: false),
                    AddressEn = table.Column<string>(type: "text", nullable: false),
                    AcceptanceClauseEn = table.Column<string>(type: "text", nullable: false),
                    AcceptanceClauseUk = table.Column<string>(type: "text", nullable: false),
                    FeesClauseEn = table.Column<string>(type: "text", nullable: false),
                    FeesClauseUk = table.Column<string>(type: "text", nullable: false),
                    TaxStatusClauseEn = table.Column<string>(type: "text", nullable: false),
                    TaxStatusClauseUk = table.Column<string>(type: "text", nullable: false),
                    SignatureImage = table.Column<byte[]>(type: "bytea", nullable: true),
                    SignatureContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SignatureUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingDetails", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_InvoicingDetails_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingPaymentDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Iban = table.Column<string>(type: "text", nullable: false),
                    BeneficiaryBank = table.Column<string>(type: "text", nullable: false),
                    Swift = table.Column<string>(type: "text", nullable: false),
                    IntermediaryBank = table.Column<string>(type: "text", nullable: false),
                    IntermediarySwift = table.Column<string>(type: "text", nullable: false),
                    IntermediaryAccount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingPaymentDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingPaymentDetails_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingPaymentDetails_UserId_Currency",
                table: "InvoicingPaymentDetails",
                columns: new[] { "UserId", "Currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicingDetails");

            migrationBuilder.DropTable(
                name: "InvoicingPaymentDetails");
        }
    }
}
