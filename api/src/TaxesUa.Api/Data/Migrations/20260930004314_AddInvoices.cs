using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NumberYear = table.Column<int>(type: "integer", nullable: true),
                    NumberSequence = table.Column<int>(type: "integer", nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    SignatureImage = table.Column<byte[]>(type: "bytea", nullable: true),
                    SignatureContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_Status", "(\"Status\" = 0 AND \"NumberSequence\" IS NULL AND \"Snapshot\" IS NULL AND \"CancelReason\" IS NULL) OR (\"Status\" = 1 AND \"NumberSequence\" IS NOT NULL AND \"Snapshot\" IS NOT NULL AND \"CancelReason\" IS NULL) OR (\"Status\" = 2 AND \"NumberSequence\" IS NOT NULL AND \"Snapshot\" IS NOT NULL AND \"CancelReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Invoices_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientId",
                table: "Invoices",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_IssueDate",
                table: "Invoices",
                columns: new[] { "UserId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_NumberYear_NumberSequence",
                table: "Invoices",
                columns: new[] { "UserId", "NumberYear", "NumberSequence" },
                unique: true,
                filter: "\"NumberSequence\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
