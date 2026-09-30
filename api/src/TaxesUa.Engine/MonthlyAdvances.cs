namespace TaxesUa.Engine;

/// <summary>
/// One kind's part of a month: what the month accrued and what of it Rule 7's allocation left unpaid.
/// </summary>
public sealed record MonthShare(long AccruedKop, long RemainingKop);

/// <summary>
/// One month under Rule 6's <c>MonthlyAdvance</c> mode. A recommendation, not an obligation: the debt
/// stays the quarter's, and this only says how much of it the owner would pay by
/// <c>RecommendedDate</c> to keep pace. <c>RecommendedKop</c> adds the kinds together because Rule 6
/// names the advance as one sum; like the tax burden it is a figure to read, never a balance.
/// </summary>
public sealed record MonthlyAdvance(
    int Year,
    int Month,
    long IncomeKop,
    MonthShare SingleTax,
    MonthShare MilitaryLevy,
    MonthShare Esv,
    DateOnly RecommendedDate)
{
    public int Quarter => (Month + 2) / 3;

    public long RecommendedKop => SingleTax.RemainingKop + MilitaryLevy.RemainingKop + Esv.RemainingKop;

    public MonthShare Of(PaymentKind kind) => kind switch
    {
        PaymentKind.SingleTax => SingleTax,
        PaymentKind.MilitaryLevy => MilitaryLevy,
        PaymentKind.Esv => Esv,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown payment kind."),
    };
}

/// <summary>
/// Rule 6's advances, read off the Rule 7 ledger rather than computed beside it, so the payment mode
/// cannot change an accrual or a balance. Each quarter's allocated payment is split over its months
/// oldest first, the order Rule 7 settles debt in, so the months' remainders add up to the quarter's
/// and nothing already paid is recommended again.
/// </summary>
public static class MonthlyAdvances
{
    /// <param name="accrual">A year the ledger allocated over.</param>
    /// <param name="recommendedDay">The year's <c>AdvanceRecommendedDay</c>, in the month after.</param>
    public static IReadOnlyList<MonthlyAdvance> ForYear(
        YearAccrual accrual, PaymentLedger ledger, int recommendedDay)
    {
        var singleTax = Split(accrual, ledger.SingleTax, month => month.SingleTaxKop);
        var militaryLevy = Split(accrual, ledger.MilitaryLevy, month => month.MilitaryLevyKop);
        var esv = Split(accrual, ledger.Esv, month => month.EsvKop);
        return
        [
            .. accrual.Months.Select(month => new MonthlyAdvance(
                accrual.Year,
                month.Month,
                month.IncomeKop,
                singleTax[month.Month - 1],
                militaryLevy[month.Month - 1],
                esv[month.Month - 1],
                new DateOnly(accrual.Year, month.Month, 1).AddMonths(1).AddDays(recommendedDay - 1))),
        ];
    }

    /// <summary>
    /// A refund month's negative accrual is credit to its quarter's other months, as Rule 7 makes a
    /// negative quarter credit to the kind; a month's remainder is never negative.
    /// </summary>
    private static MonthShare[] Split(
        YearAccrual accrual, KindLedger ledger, Func<MonthAccrual, long> accruedKop)
    {
        var ofYear = ledger.Obligations.Where(obligation => obligation.Year == accrual.Year).ToArray();
        if (ofYear.Length != accrual.Quarters.Count)
        {
            throw new ArgumentException($"The ledger did not allocate over {accrual.Year}.", nameof(ledger));
        }

        var shares = new MonthShare[12];
        foreach (var obligation in ofYear)
        {
            var months = accrual.Months.Where(month => month.Quarter == obligation.Quarter).ToArray();
            var creditKop = obligation.PaidKop + months.Sum(month => Math.Max(-accruedKop(month), 0));
            foreach (var month in months)
            {
                var owedKop = Math.Max(accruedKop(month), 0);
                var paidKop = Math.Min(owedKop, creditKop);
                creditKop -= paidKop;
                shares[month.Month - 1] = new MonthShare(accruedKop(month), owedKop - paidKop);
            }
        }

        return shares;
    }
}
