namespace TaxesUa.Engine;

/// <summary>
/// The five kinds of Rule 1 that are not income. <c>Income</c> and <c>RefundToClient</c> are absent
/// on purpose: they are cases of <see cref="TransactionInput"/>, because only they carry a sign.
/// </summary>
public enum NonIncomeKind
{
    OwnTransfer,
    FxSale,
    OwnDeposit,
    ErroneousReturn,
    Other,
}

/// <summary>
/// One account movement, already converted to hryvnia at the boundary (Rule 2). Rule 1 pairs a kind
/// with a reason only for a non-income entry, so the kinds are cases and the reason belongs to the
/// one case that has it. The private constructor stops direct construction outside the three nested
/// cases below, but it does not close the hierarchy: C# requires a non-sealed record's copy
/// constructor to be at least <c>protected</c>, so another assembly can still declare a fourth case
/// that chains through it and overrides <see cref="IncomeContributionKop"/> with an arbitrary value.
/// A switch over the three cases below is exhaustive by convention, not by the compiler.
/// </summary>
public abstract record TransactionInput
{
    private TransactionInput(DateOnly valueDate, long amountUahKop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountUahKop);
        ValueDate = valueDate;
        AmountUahKop = amountUahKop;
    }

    /// <summary>The credit date by Kyiv time, which decides the period the money lands in.</summary>
    public DateOnly ValueDate { get; }

    /// <summary>
    /// The hryvnia equivalent, fixed when the operation was recorded. Never negative: the kind
    /// carries the sign, so a negative amount here would let an income entry behave as a refund. Has
    /// no <c>init</c> accessor on purpose, so a <c>with</c> expression cannot rewrite it past the
    /// guard above.
    /// </summary>
    public long AmountUahKop { get; }

    /// <summary>Rule 1: income adds, a refund to the client subtracts, everything else contributes
    /// nothing.</summary>
    public abstract long IncomeContributionKop { get; }

    public sealed record Income(DateOnly ValueDate, long AmountUahKop)
        : TransactionInput(ValueDate, AmountUahKop)
    {
        public override long IncomeContributionKop => AmountUahKop;
    }

    /// <param name="ReceiptValueDate">The value date of the receipt this refund reverses, when the
    /// owner linked one.</param>
    public sealed record RefundToClient(
        DateOnly ValueDate,
        long AmountUahKop,
        DateOnly? ReceiptValueDate = null) : TransactionInput(ValueDate, AmountUahKop)
    {
        public override long IncomeContributionKop => -AmountUahKop;
    }

    public sealed record NonIncome(
        DateOnly ValueDate,
        long AmountUahKop,
        NonIncomeKind Kind,
        string NonIncomeReason) : TransactionInput(ValueDate, AmountUahKop)
    {
        public override long IncomeContributionKop => 0;
    }
}

/// <summary>One month's income. Negative when refunds that month outweigh receipts.</summary>
public sealed record MonthIncome(int Month, long IncomeKop);

/// <summary>
/// One quarter's income and the year's income through the end of it, which is the figure the
/// declaration is filed on (Rule 3).
/// </summary>
public sealed record QuarterIncome(int Quarter, long IncomeKop, long CumulativeIncomeKop);

/// <summary>
/// The part of a year after registration and before group 3 starts, when the FOP is on the general
/// system (Tax Code 298.1.4). <c>IncomeKop</c> is the net income of the operations left out of the
/// year for it, which the general system taxes and this engine does not.
/// </summary>
public sealed record BeforeGroup3(DateOnly From, DateOnly To, long IncomeKop);

/// <summary>
/// A year of income: all twelve months and all four quarters, present even when empty, so a caller
/// indexes instead of searching. <c>BeforeGroup3</c> is null when the year has no day between
/// registration and the start of group 3.
/// </summary>
public sealed record YearIncome(
    int Year,
    IReadOnlyList<MonthIncome> Months,
    IReadOnlyList<QuarterIncome> Quarters,
    IReadOnlyList<EngineWarning> Warnings,
    BeforeGroup3? BeforeGroup3)
{
    public long TotalIncomeKop => Quarters[^1].CumulativeIncomeKop;
}

/// <summary>
/// Income by period per Rule 1 and Rule 8 of <c>knowledge/business-rules.md</c>. Only group 3 income
/// counts: an operation from registration to the day before <see cref="FopSettingsInput.Group3Start"/>
/// is left out and summed into <see cref="YearIncome.BeforeGroup3"/> instead.
/// </summary>
public static class IncomeLedger
{
    public static YearIncome ForYear(
        int year,
        IReadOnlyList<TransactionInput> transactions,
        FopSettingsInput settings)
    {
        var monthlyKop = new long[12];
        var warnings = new List<EngineWarning>();
        var beforeGroup3Kop = 0L;

        if (settings.FopRegistrationDate is not { } registrationDate)
        {
            warnings.Add(new EngineWarning.FopRegistrationDateNotSet());
        }
        else
        {
            var group3Start = settings.Group3Start ?? registrationDate;
            foreach (var transaction in transactions)
            {
                if (transaction.ValueDate.Year != year)
                {
                    continue;
                }

                if (Exclusion(transaction, registrationDate) is { } warning)
                {
                    warnings.Add(warning);
                    continue;
                }

                if (IsBeforeGroup3(transaction, group3Start))
                {
                    beforeGroup3Kop += transaction.IncomeContributionKop;
                    continue;
                }

                monthlyKop[transaction.ValueDate.Month - 1] += transaction.IncomeContributionKop;
            }
        }

        var months = new MonthIncome[12];
        for (var month = 1; month <= 12; month++)
        {
            months[month - 1] = new MonthIncome(month, monthlyKop[month - 1]);
        }

        var quarters = new QuarterIncome[4];
        var cumulativeKop = 0L;
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var quarterKop = monthlyKop[(3 * quarter - 3)..(3 * quarter)].Sum();
            cumulativeKop += quarterKop;
            quarters[quarter - 1] = new QuarterIncome(quarter, quarterKop, cumulativeKop);
        }

        return new YearIncome(year, months, quarters, warnings, BeforeGroup3Of(year, settings, beforeGroup3Kop));
    }

    /// <summary>
    /// Whether the operation is outside group 3 because it, or the receipt a refund reverses, is dated
    /// before group 3 starts: the same transitive rule as Rule 8. Subsumes Rule 8, since group 3 never
    /// starts before registration.
    /// </summary>
    public static bool IsBeforeGroup3(TransactionInput transaction, DateOnly group3Start) =>
        transaction.ValueDate < group3Start
        || transaction is TransactionInput.RefundToClient { ReceiptValueDate: { } receiptValueDate }
            && receiptValueDate < group3Start;

    private static BeforeGroup3? BeforeGroup3Of(int year, FopSettingsInput settings, long incomeKop)
    {
        if (settings.FopRegistrationDate is not { } registered || settings.Group3Start is not { } start
            || start == registered)
        {
            return null;
        }

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var lastDay = start.AddDays(-1);
        if (registered > yearEnd || lastDay < yearStart)
        {
            return null;
        }

        return new BeforeGroup3(
            registered > yearStart ? registered : yearStart,
            lastDay < yearEnd ? lastDay : yearEnd,
            incomeKop);
    }

    /// <summary>
    /// Rule 8: why the operation is left out of income, or null when it counts. A refund linked to
    /// a receipt that predates the FOP is left out with that receipt, whatever its own date.
    /// </summary>
    public static EngineWarning? Exclusion(TransactionInput transaction, DateOnly fopRegistrationDate) =>
        transaction switch
        {
            _ when transaction.ValueDate < fopRegistrationDate =>
                new EngineWarning.OperationBeforeRegistration(transaction.ValueDate, fopRegistrationDate),
            TransactionInput.RefundToClient { ReceiptValueDate: { } receiptValueDate }
                when receiptValueDate < fopRegistrationDate =>
                new EngineWarning.RefundOfReceiptBeforeRegistration(
                    transaction.ValueDate, receiptValueDate, fopRegistrationDate),
            _ => null,
        };
}
