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

    public sealed record RefundToClient(DateOnly ValueDate, long AmountUahKop)
        : TransactionInput(ValueDate, AmountUahKop)
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
/// A year of income: all twelve months and all four quarters, present even when empty, so a caller
/// indexes instead of searching.
/// </summary>
public sealed record YearIncome(
    int Year,
    IReadOnlyList<MonthIncome> Months,
    IReadOnlyList<QuarterIncome> Quarters,
    IReadOnlyList<EngineWarning> Warnings)
{
    public long TotalIncomeKop => Quarters[^1].CumulativeIncomeKop;
}

/// <summary>
/// Income by period per Rule 1 and Rule 8 of <c>knowledge/business-rules.md</c>.
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

        if (settings.FopRegistrationDate is not { } registrationDate)
        {
            warnings.Add(new EngineWarning.FopRegistrationDateNotSet());
        }
        else
        {
            foreach (var transaction in transactions)
            {
                if (transaction.ValueDate.Year != year)
                {
                    continue;
                }

                if (transaction.ValueDate < registrationDate)
                {
                    warnings.Add(new EngineWarning.OperationBeforeRegistration(
                        transaction.ValueDate, registrationDate));
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

        return new YearIncome(year, months, quarters, warnings);
    }
}
