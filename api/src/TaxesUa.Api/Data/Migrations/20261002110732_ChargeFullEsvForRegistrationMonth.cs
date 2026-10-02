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
            // Each moved owner gets the history entry the audit interceptor would have written for the same
            // change, so the switch shows in the owner's change log.
            migrationBuilder.Sql("""
                WITH moved AS (
                    UPDATE "Settings" SET "EsvRegistrationMonthPolicy" = 0
                    WHERE "EsvRegistrationMonthPolicy" = 1
                    RETURNING *),
                snapshot AS (
                    SELECT "UserId", jsonb_build_object(
                        'backOnGroup3FromQuarter', "BackOnGroup3FromQuarter",
                        'backOnGroup3FromYear', "BackOnGroup3FromYear",
                        'defaultCurrency', "DefaultCurrency",
                        'esvExempt', "EsvExempt",
                        'fopRegistrationDate', "FopRegistrationDate",
                        'locale', "Locale",
                        'paymentMode', CASE "PaymentMode" WHEN 0 THEN 'Quarterly' ELSE 'MonthlyAdvance' END,
                        'shiftTaxPaymentFromWeekend', "ShiftTaxPaymentFromWeekend",
                        'taxPaymentCountsFromStatutoryDeclarationDate', "TaxPaymentCountsFromStatutoryDeclarationDate",
                        'theme', "Theme",
                        'weekendDays', to_jsonb(ARRAY(
                            SELECT (ARRAY['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'])[day + 1]
                            FROM unnest("WeekendDays") WITH ORDINALITY AS days(day, position)
                            ORDER BY position))) AS fields
                    FROM moved)
                INSERT INTO "AuditLog" ("UserId", "At", "Entity", "EntityId", "Action", "Before", "After")
                SELECT "UserId", now(), 'Settings', "UserId", 'Update',
                    fields || '{"esvRegistrationMonthPolicy": "Prorated"}',
                    fields || '{"esvRegistrationMonthPolicy": "FullMonth"}'
                FROM snapshot;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A moved row cannot be told from one that was always FullMonth.
        }
    }
}
