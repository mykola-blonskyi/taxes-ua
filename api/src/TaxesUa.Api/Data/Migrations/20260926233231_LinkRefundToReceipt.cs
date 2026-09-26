using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkRefundToReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RefundsTransactionId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RefundsTransactionId",
                table: "Transactions",
                column: "RefundsTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_RefundsTransactionId",
                table: "Transactions",
                column: "RefundsTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_RefundsTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RefundsTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RefundsTransactionId",
                table: "Transactions");
        }
    }
}
