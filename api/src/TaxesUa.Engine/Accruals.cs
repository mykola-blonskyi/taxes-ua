namespace TaxesUa.Engine;

/// <summary>
/// What ESV the month of registration costs. Rule 3 calls the full amount the default and leaves the
/// alternative to be confirmed, so <c>Prorated</c> charges the month by its active days. The owner
/// still has to settle that: ESV for oneself is a fixed monthly sum, so a part-month accrual leaves
/// Rule 7 with a balance no payment clears, and the alternative may be a month charged at zero.
/// </summary>
public enum EsvRegistrationMonthPolicy
{
    FullMonth,
    Prorated,
}

/// <summary>
/// One quarter of the declaration. The cumulative figures are what the declaration carries; the
/// per-quarter tax is the cumulative figure minus what the earlier quarters already accrued, and is
/// negative when refunds shrank the cumulative income (Rule 3, settled per Rule 7). The single tax
/// includes Rule 4's excess tax: <c>CumulativeExcessIncomeKop</c> is the income through the quarter
/// over the year's limit and <c>CumulativeExcessTaxKop</c> its tax at the excess rate, both zero in
/// every quarter but the one the limit is crossed in, since no later quarter is accrued. The cumulative
/// figures run from 1 January, or from the quarter the owner is back on group 3 from.
/// </summary>
public sealed record QuarterAccrual(
    QuarterIncome Income,
    long SingleTaxKop,
    long CumulativeSingleTaxKop,
    long CumulativeExcessIncomeKop,
    long CumulativeExcessTaxKop,
    long MilitaryLevyKop,
    long CumulativeMilitaryLevyKop,
    long EsvKop)
{
    public long TotalKop => SingleTaxKop + MilitaryLevyKop + EsvKop;
}

/// <summary>
/// One month's share of its quarter's accruals, for the monthly advances of Rule 6. The single tax and
/// the levy are the year-to-date tax through this month minus that through the month before, the way
/// the declaration splits quarters, so a quarter's three months add up to its accrual to the kopeck:
/// advances paid in full clear the quarter without a rounding remainder.
/// </summary>
public sealed record MonthAccrual(
    int Month,
    long IncomeKop,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop)
{
    public int Quarter => (Month + 2) / 3;
}

/// <summary>A quarter of a year, ordered by year and then by quarter.</summary>
public sealed record YearQuarter(int Year, int Quarter) : IComparable<YearQuarter>
{
    public int CompareTo(YearQuarter? other) =>
        other is null ? 1 : (Year, Quarter).CompareTo((other.Year, other.Quarter));

    public static bool operator <(YearQuarter left, YearQuarter right) => left.CompareTo(right) < 0;

    public static bool operator >(YearQuarter left, YearQuarter right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearQuarter left, YearQuarter right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearQuarter left, YearQuarter right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Rule 4: the income went over the limit in <c>Quarter</c> of <c>Year</c>, so group 3 ends with that
/// quarter and the FOP must be on another system from <c>SwitchFromQuarter</c> of <c>SwitchFromYear</c>,
/// which is the next year's first quarter when the limit is crossed in Q4.
/// </summary>
public sealed record LimitCrossing(int Year, int Quarter)
{
    public YearQuarter At => new(Year, Quarter);

    public int SwitchFromYear => Quarter == 4 ? Year + 1 : Year;

    public int SwitchFromQuarter => Quarter == 4 ? 1 : Quarter + 1;
}

/// <summary>
/// A year of accruals, per month and per quarter. <c>Income</c> holds every month and quarter of the
/// year, but <c>Quarters</c> and <c>Months</c> hold only those in group 3 (Rule 4): a quarter after a
/// crossing, in this year or an earlier one, belongs to a system this engine does not compute, so it has
/// no accrual rather than a group 3 figure that would be wrong, until the owner's
/// <see cref="FopSettingsInput.BackOnGroup3From"/>. <c>LimitCrossing</c> is the crossing that ends group 3
/// in this year, or else the earlier one that keeps a quarter of this year out of it.
/// <c>StoppedAtYearEnd</c> is the crossing group 3 is still stopped by after Q4, which the next year
/// inherits.
/// </summary>
public sealed record YearAccrual(
    int Year,
    YearIncome Income,
    IReadOnlyList<MonthAccrual> Months,
    IReadOnlyList<QuarterAccrual> Quarters,
    LimitCrossing? LimitCrossing,
    LimitCrossing? StoppedAtYearEnd,
    IReadOnlyList<EngineWarning> Warnings)
{
    public bool InGroup3(int quarter) => Quarters.Any(accrual => accrual.Income.Quarter == quarter);

    /// <summary>The quarter has to be in group 3.</summary>
    public QuarterAccrual QuarterOf(int quarter) => Quarters.Single(accrual => accrual.Income.Quarter == quarter);

    /// <summary>
    /// Stops at <paramref name="quarter"/> because ESV accrues every month of the year up front: the
    /// whole year's ESV over part of a year's income would overstate the rate until December. The
    /// quarter has to be in group 3.
    /// </summary>
    public TaxBurden BurdenThrough(int quarter) => new(
        QuarterOf(quarter).Income.CumulativeIncomeKop,
        Quarters.Where(accrual => accrual.Income.Quarter <= quarter).Sum(accrual => accrual.TotalKop));
}

/// <summary>
/// Everything accrued against the income it was accrued on. A statistic, not a ledger: Rule 7 keeps
/// balances apart by kind, and nothing here is paid or owed.
/// </summary>
public sealed record TaxBurden(long IncomeKop, long TaxKop)
{
    /// <summary>Null without income, where ESV alone has no rate to be a share of.</summary>
    public long? RateBp => IncomeKop > 0 ? Money.ShareBp(TaxKop, IncomeKop) : null;
}

/// <summary>One year's input to <see cref="Accruals.ForYears"/>.</summary>
public sealed record AccrualYearInput(
    int Year, IReadOnlyList<TransactionInput> Transactions, TaxYearConfigInput Config);

/// <summary>
/// The single tax, the military levy and ESV for a year, per Rule 3 of
/// <c>knowledge/business-rules.md</c>. Pure: the year, the rates and the registration date all
/// arrive as arguments.
/// </summary>
public static class Accruals
{
    /// <summary>
    /// The years in order, each inheriting the crossing the one before stopped group 3 by (Rule 4). A
    /// year missing from <paramref name="years"/> passes the stop on unchanged, since only the owner's
    /// <see cref="FopSettingsInput.BackOnGroup3From"/> lifts it.
    /// </summary>
    public static IReadOnlyList<YearAccrual> ForYears(
        IReadOnlyList<AccrualYearInput> years, FopSettingsInput settings)
    {
        var accruals = new List<YearAccrual>(years.Count);
        LimitCrossing? stoppedBy = null;
        foreach (var year in years.OrderBy(year => year.Year))
        {
            var accrual = ForYear(year.Year, year.Transactions, year.Config, settings, stoppedBy);
            accruals.Add(accrual);
            stoppedBy = accrual.StoppedAtYearEnd;
        }

        return accruals;
    }

    /// <param name="stoppedBy">The crossing of an earlier year group 3 is still stopped by on 1 January,
    /// or null when the year starts in group 3.</param>
    public static YearAccrual ForYear(
        int year,
        IReadOnlyList<TransactionInput> transactions,
        TaxYearConfigInput config,
        FopSettingsInput settings,
        LimitCrossing? stoppedBy = null)
    {
        var income = IncomeLedger.ForYear(year, transactions, settings);
        var esvByMonthKop = EsvByMonth(year, config, settings);
        var warnings = new List<EngineWarning>(income.Warnings);

        var quarters = new List<QuarterAccrual>(4);
        var months = new List<MonthAccrual>(12);
        LimitCrossing? crossing = null;
        var stop = stoppedBy;
        var cumulativeIncomeKop = 0L;
        var accruedSingleTaxKop = 0L;
        var accruedMilitaryLevyKop = 0L;
        foreach (var quarterIncome in income.Quarters)
        {
            var quarter = quarterIncome.Quarter;
            if (stop is not null && settings.BackOnGroup3From is { } back
                && back > stop.At && back <= new YearQuarter(year, quarter))
            {
                stop = null;
                cumulativeIncomeKop = 0;
                accruedSingleTaxKop = 0;
                accruedMilitaryLevyKop = 0;
            }

            if (stop is not null)
            {
                continue;
            }

            var quarterStartSingleTaxKop = accruedSingleTaxKop;
            var quarterStartMilitaryLevyKop = accruedMilitaryLevyKop;
            foreach (var month in income.Months.Skip(3 * quarter - 3).Take(3))
            {
                cumulativeIncomeKop += month.IncomeKop;
                var singleTaxKop = SingleTaxOn(cumulativeIncomeKop, config);
                var militaryLevyKop = Money.ApplyBp(cumulativeIncomeKop, config.MilitaryLevyRateBp);
                months.Add(new MonthAccrual(
                    month.Month,
                    month.IncomeKop,
                    singleTaxKop - accruedSingleTaxKop,
                    militaryLevyKop - accruedMilitaryLevyKop,
                    esvByMonthKop[month.Month - 1]));
                accruedSingleTaxKop = singleTaxKop;
                accruedMilitaryLevyKop = militaryLevyKop;
            }

            if (accruedSingleTaxKop < 0 || accruedMilitaryLevyKop < 0)
            {
                warnings.Add(new EngineWarning.NegativeCumulativeTax(quarter));
            }

            var excessIncomeKop = Math.Max(cumulativeIncomeKop - config.IncomeLimitKop, 0);
            quarters.Add(new QuarterAccrual(
                quarterIncome with { CumulativeIncomeKop = cumulativeIncomeKop },
                accruedSingleTaxKop - quarterStartSingleTaxKop,
                accruedSingleTaxKop,
                excessIncomeKop,
                Money.ApplyBp(excessIncomeKop, config.ExcessRateBp),
                accruedMilitaryLevyKop - quarterStartMilitaryLevyKop,
                accruedMilitaryLevyKop,
                esvByMonthKop[(3 * quarter - 3)..(3 * quarter)].Sum()));

            if (excessIncomeKop > 0)
            {
                crossing = new LimitCrossing(year, quarter);
                stop = crossing;
            }
        }

        return new YearAccrual(
            year,
            income,
            months,
            quarters,
            crossing ?? (quarters.Count < 4 ? stoppedBy : null),
            stop,
            warnings);
    }

    /// <summary>
    /// Rule 4: the single tax rate up to the limit and the excess rate above it, each rounded once. A
    /// month and a quarter both split this one function of the period's income so far, so they add up to
    /// the kopeck.
    /// </summary>
    private static long SingleTaxOn(long cumulativeIncomeKop, TaxYearConfigInput config) =>
        Money.ApplyBp(Math.Min(cumulativeIncomeKop, config.IncomeLimitKop), config.SingleTaxRateBp)
        + Money.ApplyBp(Math.Max(cumulativeIncomeKop - config.IncomeLimitKop, 0), config.ExcessRateBp);

    private static long[] EsvByMonth(
        int year,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        var esvKop = new long[12];
        if (settings.EsvExempt || settings.FopRegistrationDate is not { } registrationDate)
        {
            return esvKop;
        }

        var monthKop = Money.ApplyBp(config.MinWageKop, config.EsvRateBp);
        for (var month = 1; month <= 12; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd < registrationDate)
            {
                continue;
            }

            esvKop[month - 1] = monthStart >= registrationDate
                ? monthKop
                : RegistrationMonthKop(monthKop, registrationDate, monthEnd, settings);
        }

        return esvKop;
    }

    private static long RegistrationMonthKop(
        long monthKop,
        DateOnly registrationDate,
        DateOnly monthEnd,
        FopSettingsInput settings) =>
        settings.EsvRegistrationMonthPolicy switch
        {
            EsvRegistrationMonthPolicy.FullMonth => monthKop,
            EsvRegistrationMonthPolicy.Prorated => Money.Prorate(
                monthKop, monthEnd.Day - registrationDate.Day + 1, monthEnd.Day),
            var policy => throw new ArgumentOutOfRangeException(
                nameof(settings), policy, "Unknown ESV registration-month policy."),
        };
}
