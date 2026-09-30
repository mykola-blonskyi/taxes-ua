using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// Whether a quarter's declaration can be filed from what the app holds (Rule 15), computed on read
/// from rows the caller already loaded. Each item is one field the interface words; the api sends no
/// text, per ADR-002.
/// </summary>
internal static class DeclarationReadiness
{
    /// <param name="ledger">Rule 7's ledger, or null outside it, where nothing counts as unpaid.</param>
    public static DeclarationReadinessResponse Evaluate(
        int year,
        int quarter,
        int receiptsToReview,
        int pendingPaymentCandidates,
        bool taxYearVerified,
        bool registrationDateSet,
        InvoicingDetails? invoicing,
        DeclarationDetails? details,
        bool incomeOverLimit,
        PaymentLedger? ledger)
    {
        var missingDetails = DeclarationDetails.Missing(invoicing, details);
        var ready = receiptsToReview == 0
            && pendingPaymentCandidates == 0
            && taxYearVerified
            && registrationDateSet
            && missingDetails.Length == 0
            && !incomeOverLimit;

        return new DeclarationReadinessResponse(
            receiptsToReview,
            pendingPaymentCandidates,
            taxYearVerified,
            registrationDateSet,
            missingDetails,
            incomeOverLimit,
            new UnpaidResponse(
                Remaining(ledger?.SingleTax),
                Remaining(ledger?.MilitaryLevy),
                Remaining(ledger?.Esv)),
            ready);

        long Remaining(KindLedger? kind) => kind?.Obligations
            .Where(obligation => obligation.Year == year && obligation.Quarter <= quarter)
            .Sum(obligation => obligation.RemainingKop) ?? 0;
    }
}

/// <summary>
/// Every item but <c>Unpaid</c> blocks <c>Ready</c>: receipts or payment candidates left to review,
/// an unverified tax year, no registration date, a missing detail, and income over the limit, whose
/// 15% lines the app does not fill yet (#118). <c>ReceiptsToReview</c> counts the year's imports
/// through the quarter's end; <c>PendingPaymentCandidates</c> counts every pending one, whatever its
/// year.
/// </summary>
internal sealed record DeclarationReadinessResponse(
    int ReceiptsToReview,
    int PendingPaymentCandidates,
    bool TaxYearVerified,
    bool RegistrationDateSet,
    DeclarationDetailField[] MissingDetails,
    bool IncomeOverLimit,
    UnpaidResponse Unpaid,
    bool Ready);

/// <summary>
/// A warning only: what the year's obligations through the quarter still owe per kind, as Rule 7
/// allocated the payments. Paying is not part of filing, so it never blocks the declaration.
/// </summary>
internal sealed record UnpaidResponse(long SingleTaxKop, long MilitaryLevyKop, long EsvKop);
