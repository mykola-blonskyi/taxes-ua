namespace TaxesUa.Engine;

/// <summary>
/// The three payments into the budget, mirroring <c>BudgetPayment.Kind</c>. An enum and not a record
/// union because nothing about a payment varies by kind: Rule 7 keeps the three ledgers apart, and
/// <see cref="YearBalances"/> does that by naming one field per kind rather than by branching on this
/// value.
/// </summary>
public enum PaymentKind
{
    SingleTax,
    MilitaryLevy,
    Esv,
}

/// <summary>
/// The period a payment is recorded against. The <c>BudgetPayment</c> entity spells this as a
/// nullable <c>PeriodQuarter</c> beside a nullable <c>PeriodMonth</c>, which admits a row with
/// neither and a row with both; as cases, neither is representable. Rule 6 credits an advance against
/// the quarterly obligation, so both cases answer <see cref="Quarter"/> and the balance arithmetic
/// never asks which case it is holding.
/// </summary>
public abstract record PaymentPeriod
{
    private PaymentPeriod(int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        Quarter = quarter;
    }

    /// <summary>
    /// The quarter this payment credits. Has no <c>init</c> accessor on purpose, so a <c>with</c>
    /// expression cannot rewrite it past the guard above.
    /// </summary>
    public int Quarter { get; }

    public sealed record Quarterly : PaymentPeriod
    {
        public Quarterly(int quarter) : base(quarter)
        {
        }
    }

    public sealed record Monthly : PaymentPeriod
    {
        public Monthly(int month) : base(QuarterOf(month)) => Month = month;

        /// <summary>
        /// The month paid for. Kept beside the quarter it credits because the monthly advances of
        /// Rule 6 reconcile against the month.
        /// </summary>
        public int Month { get; }
    }

    /// <summary>
    /// A month outside 1 to 12 already maps to a quarter the constructor above rejects, so these two
    /// guards exist for the name they report: a caller that passed a month has to be told the month
    /// is wrong, not the quarter it was turned into.
    /// </summary>
    private static int QuarterOf(int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return (month + 2) / 3;
    }
}

/// <summary>
/// One payment into the budget, mirroring the <c>BudgetPayment</c> entity. Its <c>PaidOn</c>,
/// <c>Note</c> and <c>CreatedAt</c> are absent: Rule 7 attributes a payment by the period it is
/// recorded against and never by the day it left the account, so a payment made in February for the
/// previous Q4 settles Q4.
/// </summary>
public sealed record BudgetPaymentInput
{
    public BudgetPaymentInput(
        PaymentKind kind, long amountKop, int periodYear, PaymentPeriod period)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountKop);
        Kind = kind;
        AmountKop = amountKop;
        PeriodYear = periodYear;
        Period = period;
    }

    public PaymentKind Kind { get; }

    /// <summary>
    /// Has no <c>init</c> accessor on purpose, so a <c>with</c> expression cannot slip past the guard
    /// above and turn a payment into a withdrawal from the budget, which Rule 7 does not model.
    /// </summary>
    public long AmountKop { get; }

    public int PeriodYear { get; }

    public PaymentPeriod Period { get; }
}

/// <summary>
/// One quarter of one kind's ledger. <c>OpeningBalanceKop</c> is the previous quarter's
/// <c>BalanceKop</c> and is where Rule 7's carry-forward lives: negative when an earlier quarter of
/// the same kind was overpaid, positive when it still owes. <c>BalanceKop</c> equals
/// <c>OpeningBalanceKop + AccruedKop - PaidKop</c>, which makes it cumulative accrued minus
/// cumulative paid with no second pair of running totals that could contradict it. Positive is owed,
/// negative is overpaid.
/// </summary>
public sealed record QuarterBalance(
    int Quarter,
    long AccruedKop,
    long PaidKop,
    long OpeningBalanceKop,
    long BalanceKop);

/// <summary>
/// One kind's ledger for the year, all four quarters present even when nothing accrued.
/// <c>ClosingBalanceKop</c> is what the year ends on, and is where an overpayment larger than the
/// whole year's liability for this kind comes to rest: the year has no further quarter to absorb it.
/// </summary>
public sealed record KindBalance(PaymentKind Kind, IReadOnlyList<QuarterBalance> Quarters)
{
    public long ClosingBalanceKop => Quarters[^1].BalanceKop;
}

/// <summary>
/// The year's balances. Rule 7 forbids mixing kinds, so the three ledgers are three named fields and
/// not a collection: there is nothing here to iterate or sum across, so a single pooled figure cannot
/// be formed by accident. The warnings are the accrual's, forwarded so that a caller holds one object.
/// </summary>
public sealed record YearBalances(
    int Year,
    KindBalance SingleTax,
    KindBalance MilitaryLevy,
    KindBalance Esv,
    IReadOnlyList<EngineWarning> Warnings);

/// <summary>
/// Accrued against paid per kind, per Rule 7 of <c>knowledge/business-rules.md</c>. Every cumulative
/// figure is derived here from the per-quarter accruals, so nothing reads
/// <c>QuarterAccrual.CumulativeSingleTaxKop</c>, <c>CumulativeMilitaryLevyKop</c> or
/// <c>TotalKop</c>: ESV has no cumulative counterpart to pair with, and a mix of one kind's delta
/// with another's absolute is the one arithmetic mistake this file could make silently.
/// </summary>
public static class Balances
{
    public static YearBalances ForYear(
        YearAccrual accrual,
        IReadOnlyList<BudgetPaymentInput> payments) =>
        new(
            accrual.Year,
            ForKind(PaymentKind.SingleTax, accrual, quarter => quarter.SingleTaxKop, payments),
            ForKind(PaymentKind.MilitaryLevy, accrual, quarter => quarter.MilitaryLevyKop, payments),
            ForKind(PaymentKind.Esv, accrual, quarter => quarter.EsvKop, payments),
            accrual.Warnings);

    private static KindBalance ForKind(
        PaymentKind kind,
        YearAccrual accrual,
        Func<QuarterAccrual, long> accruedKop,
        IReadOnlyList<BudgetPaymentInput> payments)
    {
        var quarters = new QuarterBalance[4];
        var openingBalanceKop = 0L;
        foreach (var quarter in accrual.Quarters)
        {
            var accruedThisQuarterKop = accruedKop(quarter);
            var paidKop = payments
                .Where(payment =>
                    payment.Kind == kind
                    && payment.PeriodYear == accrual.Year
                    && payment.Period.Quarter == quarter.Income.Quarter)
                .Sum(payment => payment.AmountKop);
            var balanceKop = openingBalanceKop + accruedThisQuarterKop - paidKop;

            quarters[quarter.Income.Quarter - 1] = new QuarterBalance(
                quarter.Income.Quarter,
                accruedThisQuarterKop,
                paidKop,
                openingBalanceKop,
                balanceKop);
            openingBalanceKop = balanceKop;
        }

        return new KindBalance(kind, quarters);
    }
}
