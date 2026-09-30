namespace TaxesUa.Engine;

/// <summary>
/// The single tax and the military levy to set aside from one receipt, at the rates of the year of its
/// own date. Negative for a refund, which releases reserve at the refund year's rates, whatever year the
/// receipt it reverses was taxed in. A receipt knows nothing of the limit, so the excess rate of Rule 4
/// is carried by the total of <see cref="TaxReserve.Needed"/>, not by this figure.
/// </summary>
public sealed record SetAside(long SingleTaxKop, long MilitaryLevyKop);

/// <summary>What must be there by one due date, per kind. The kinds are added only in <c>TotalKop</c>.</summary>
public sealed record ReserveDue(
    DateOnly DueDate,
    ObligationStatus Status,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop)
{
    public long TotalKop => SingleTaxKop + MilitaryLevyKop + EsvKop;
}

/// <summary>
/// What the taxes need now, grouped by due date, oldest first. A derived figure: it adds kinds
/// together, which Rule 7 forbids for a balance, so it is never fed back into one.
/// </summary>
public sealed record ReserveNeed(IReadOnlyList<ReserveDue> Dues)
{
    public long TotalKop => Dues.Sum(due => due.TotalKop);
}

/// <summary>
/// The tax reserve of Rule 13 of <c>knowledge/business-rules.md</c>: figures read off the accruals and
/// the Rule 7 allocation, never a tax rule of their own. Nothing reads a clock; <c>today</c> is an
/// argument.
/// </summary>
public static class TaxReserve
{
    /// <summary>
    /// Null when Rule 8 leaves the operation out of income. A non-income kind sets aside zero. Each
    /// receipt rounds on its own, so the receipts of a quarter can differ from its accrual by a kopeck.
    /// </summary>
    public static SetAside? SetAsideFor(
        TransactionInput transaction, TaxYearConfigInput config, DateOnly registrationDate) =>
        IncomeLedger.Exclusion(transaction, registrationDate) is not null
            ? null
            : new SetAside(
                Money.ApplyBp(transaction.IncomeContributionKop, config.SingleTaxRateBp),
                Money.ApplyBp(transaction.IncomeContributionKop, config.MilitaryLevyRateBp));

    /// <summary>
    /// Per kind, what has accrued to <paramref name="today"/> minus what the allocation put against it,
    /// grouped by the obligation's due date. A past quarter accrued in full, the current one accrued
    /// its tax on the income so far and its ESV for the months begun, and a later one has accrued
    /// nothing, so money paid ahead of it does not reduce what is needed now.
    /// </summary>
    public static ReserveNeed Needed(PaymentLedger ledger, IReadOnlyList<LedgerYear> years, DateOnly today)
    {
        var months = years.ToDictionary(year => year.Accrual.Year, year => year.Accrual.Months);
        var rows = new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }
            .SelectMany(kind => kind.Obligations.Select(obligation => (
                obligation.DueDate,
                obligation.Kind,
                NeededKop: Math.Max(AccruedToDate(obligation, months[obligation.Year], today) - obligation.PaidKop, 0))))
            .Where(row => row.NeededKop > 0)
            .ToArray();

        return new ReserveNeed(
        [
            .. rows
                .GroupBy(row => row.DueDate)
                .OrderBy(group => group.Key)
                .Select(group => new ReserveDue(
                    group.Key,
                    group.Key < today ? ObligationStatus.Overdue
                        : group.Key == today ? ObligationStatus.Due : ObligationStatus.Upcoming,
                    Of(group, PaymentKind.SingleTax),
                    Of(group, PaymentKind.MilitaryLevy),
                    Of(group, PaymentKind.Esv))),
        ]);

        static long Of(IEnumerable<(DateOnly DueDate, PaymentKind Kind, long NeededKop)> rows, PaymentKind kind) =>
            rows.Where(row => row.Kind == kind).Sum(row => row.NeededKop);
    }

    private static long AccruedToDate(Obligation obligation, IReadOnlyList<MonthAccrual> months, DateOnly today)
    {
        var quarterStart = new DateOnly(obligation.Year, (3 * obligation.Quarter) - 2, 1);
        if (today < quarterStart)
        {
            return 0;
        }

        if (obligation.Kind != PaymentKind.Esv || today >= quarterStart.AddMonths(3))
        {
            return Math.Max(obligation.AccruedKop, 0);
        }

        return months
            .Where(month => month.Quarter == obligation.Quarter && new DateOnly(obligation.Year, month.Month, 1) <= today)
            .Sum(month => Math.Max(month.EsvKop, 0));
    }
}
