using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxesUa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargeFullEsvForRegistrationMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The prorated default rested on a wrong premise: the law has no part-month minimum ESV (ADR-018).
            migrationBuilder.Sql("UPDATE \"Settings\" SET \"EsvRegistrationMonthPolicy\" = 0 WHERE \"EsvRegistrationMonthPolicy\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A moved row cannot be told from one that was always FullMonth.
        }
    }
}
