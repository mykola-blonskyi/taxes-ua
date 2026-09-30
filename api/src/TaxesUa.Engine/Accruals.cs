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
/// every quarter but the one the limit is crossed in, since no later quarter is accrued.
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

/// <summary>
/// Rule 4: the year's income went over its limit in <c>Quarter</c>, so group 3 ends with that quarter
/// and the FOP must be on another system from <c>SwitchFromQuarter</c> of <c>SwitchFromYear</c>, which
/// is the next year's first quarter when the limit is crossed in Q4.
/// </summary>
public sealed record LimitCrossing(int Year, int Quarter)
{
    public int SwitchFromYear => Quarter == 4 ? Year + 1 : Year;

    public int SwitchFromQuarter => Quarter == 4 ? 1 : Quarter + 1;
}

/// <summary>
/// A year of accruals, per month and per quarter. <c>Income</c> holds every month and quarter of the
/// year, but <c>Quarters</c> and <c>Months</c> stop at the quarter the limit is crossed in (Rule 4):
/// group 3 ends there, and a later quarter's tax belongs to a system this engine does not compute, so
/// it has no accrual rather than a group 3 figure that would be wrong.
/// </summary>
public sealed record YearAccrual(
    int Year,
    YearIncome Income,
    IReadOnlyList<MonthAccrual> Months,
    IReadOnlyList<QuarterAccrual> Quarters,
    IReadOnlyList<EngineWarning> Warnings)
{
    public LimitCrossing? LimitCrossing =>
        Quarters[^1].CumulativeExcessIncomeKop > 0 ? new LimitCrossing(Year, Quarters.Count) : null;

    public bool InGroup3(int quarter) => quarter <= Quarters.Count;

    /// <summary>
    /// Stops at <paramref name="quarter"/> because ESV accrues every month of the year up front: the
    /// whole year's ESV over part of a year's income would overstate the rate until December. The
    /// quarter has to be in group 3.
    /// </summary>
    public TaxBurden BurdenThrough(int quarter) => new(
        Quarters[quarter - 1].Income.CumulativeIncomeKop,
        Quarters.Take(quarter).Sum(accrual => accrual.TotalKop));
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

/// <summary>
/// The single tax, the military levy and ESV for a year, per Rule 3 of
/// <c>knowledge/business-rules.md</c>. Pure: the year, the rates and the registration date all
/// arrive as arguments.
/// </summary>
public static class Accruals
{
    public static YearAccrual ForYear(
        int year,
        IReadOnlyList<TransactionInput> transactions,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        var income = IncomeLedger.ForYear(year, transactions, settings);
        var esvByMonthKop = EsvByMonth(year, config, settings);
        var warnings = new List<EngineWarning>(income.Warnings);

        var quarters = new List<QuarterAccrual>(4);
        var accruedSingleTaxKop = 0L;
        var accruedMilitaryLevyKop = 0L;
        foreach (var quarterIncome in income.Quarters)
        {
            var cumulativeIncomeKop = quarterIncome.CumulativeIncomeKop;
            var excessIncomeKop = Math.Max(cumulativeIncomeKop - config.IncomeLimitKop, 0);
            var cumulativeSingleTaxKop = SingleTaxOn(cumulativeIncomeKop, config);
            var cumulativeMilitaryLevyKop = Money.ApplyBp(cumulativeIncomeKop, config.MilitaryLevyRateBp);

            if (cumulativeSingleTaxKop < 0 || cumulativeMilitaryLevyKop < 0)
            {
                warnings.Add(new EngineWarning.NegativeCumulativeTax(quarterIncome.Quarter));
            }

            quarters.Add(new QuarterAccrual(
                quarterIncome,
                cumulativeSingleTaxKop - accruedSingleTaxKop,
                cumulativeSingleTaxKop,
                excessIncomeKop,
                Money.ApplyBp(excessIncomeKop, config.ExcessRateBp),
                cumulativeMilitaryLevyKop - accruedMilitaryLevyKop,
                cumulativeMilitaryLevyKop,
                esvByMonthKop[(3 * quarterIncome.Quarter - 3)..(3 * quarterIncome.Quarter)].Sum()));

            accruedSingleTaxKop = cumulativeSingleTaxKop;
            accruedMilitaryLevyKop = cumulativeMilitaryLevyKop;
            if (excessIncomeKop > 0)
            {
                break;
            }
        }

        var months = MonthsOf(income, esvByMonthKop, config).Take(3 * quarters.Count).ToArray();
        return new YearAccrual(year, income, months, quarters, warnings);
    }

    /// <summary>
    /// Rule 4: the single tax rate up to the limit and the excess rate above it, each rounded once. A
    /// month and a quarter both split this one function of year-to-date income, so they add up to the
    /// kopeck.
    /// </summary>
    private static long SingleTaxOn(long cumulativeIncomeKop, TaxYearConfigInput config) =>
        Money.ApplyBp(Math.Min(cumulativeIncomeKop, config.IncomeLimitKop), config.SingleTaxRateBp)
        + Money.ApplyBp(Math.Max(cumulativeIncomeKop - config.IncomeLimitKop, 0), config.ExcessRateBp);

    private static MonthAccrual[] MonthsOf(YearIncome income, long[] esvByMonthKop, TaxYearConfigInput config)
    {
        var months = new MonthAccrual[12];
        var cumulativeIncomeKop = 0L;
        var accruedSingleTaxKop = 0L;
        var accruedMilitaryLevyKop = 0L;
        foreach (var month in income.Months)
        {
            cumulativeIncomeKop += month.IncomeKop;
            var cumulativeSingleTaxKop = SingleTaxOn(cumulativeIncomeKop, config);
            var cumulativeMilitaryLevyKop = Money.ApplyBp(cumulativeIncomeKop, config.MilitaryLevyRateBp);
            months[month.Month - 1] = new MonthAccrual(
                month.Month,
                month.IncomeKop,
                cumulativeSingleTaxKop - accruedSingleTaxKop,
                cumulativeMilitaryLevyKop - accruedMilitaryLevyKop,
                esvByMonthKop[month.Month - 1]);
            accruedSingleTaxKop = cumulativeSingleTaxKop;
            accruedMilitaryLevyKop = cumulativeMilitaryLevyKop;
        }

        return months;
    }

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
