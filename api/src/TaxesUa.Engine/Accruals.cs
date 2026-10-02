namespace TaxesUa.Engine;

/// <summary>
/// What ESV the month of registration costs. The law has no part-month minimum and the DPS charges the
/// full monthly minimum for the month a FOP registers (Rule 3), so <c>FullMonth</c> is the reading that
/// matches it. <c>Prorated</c>, the month by its active days, is kept only as a setting that does not
/// match the law.
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
/// figures run from 1 January, or from the quarter the owner is back on group 3 from. A quarter with
/// <c>Group3</c> false is over before group 3 starts (Tax Code 298.1.4): ESV does not depend on the tax
/// system, so it still accrues ESV from registration, but no single tax or levy and no declaration.
/// </summary>
public sealed record QuarterAccrual(
    QuarterIncome Income,
    long SingleTaxKop,
    long CumulativeSingleTaxKop,
    long CumulativeExcessIncomeKop,
    long CumulativeExcessTaxKop,
    long MilitaryLevyKop,
    long CumulativeMilitaryLevyKop,
    long EsvKop,
    bool Group3 = true)
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
/// One month's ESV for oneself (Rule 3): <c>BaseKop</c> is the minimum wage, or, only under the
/// non-statutory <see cref="EsvRegistrationMonthPolicy.Prorated"/>, its share for the registration
/// month's active days, and the ESV is the rate on it, so annex 1's column 4 is column 2 times column 3.
/// </summary>
public sealed record EsvMonth(int Month, long BaseKop, int RateBp)
{
    public long EsvKop => Money.ApplyBp(BaseKop, RateBp);
}

/// <summary>
/// Annex 1 to the declaration (form F0133109), per Rule 15: the ESV of every group 3 month of the year
/// that owes it, filed with the year's last group 3 declaration, the one for <c>Quarter</c>.
/// <c>From</c> to <c>To</c> is the stretch on the simplified system it covers (the annex's item 8).
/// <c>LeavesGroup3</c> marks a declaration for the quarter the limit was crossed in, after which the
/// FOP moves to other taxes: the annex's "перехід на сплату інших податків і зборів".
/// </summary>
public sealed record EsvAnnex(
    int Year,
    int Quarter,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<EsvMonth> Months,
    bool LeavesGroup3)
{
    public long BaseKop => Months.Sum(month => month.BaseKop);

    public long EsvKop => Months.Sum(month => month.EsvKop);
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
/// year, but <c>Quarters</c> and <c>Months</c> hold only those accrued (Rule 4): a quarter after a
/// crossing, in this year or an earlier one, belongs to a system this engine does not compute, so it has
/// no accrual rather than a group 3 figure that would be wrong, until the owner's
/// <see cref="FopSettingsInput.BackOnGroup3From"/>. A quarter that ends before
/// <see cref="FopSettingsInput.Group3Start"/> is accrued for its ESV only and is not
/// <see cref="InGroup3"/>. <c>LimitCrossing</c> is the crossing that ends group 3
/// in this year, or else the earlier one that keeps a quarter of this year out of it.
/// <c>StoppedAtYearEnd</c> is the crossing group 3 is still stopped by after Q4, which the next year
/// inherits. <c>EsvAnnex</c> is the year's ESV for every accrued month, before group 3 included, as
/// annex 1 of the year's last group 3 declaration reports it (Rule 15); null when no month of the year
/// owes ESV or the year has no group 3 declaration to carry it.
/// </summary>
public sealed record YearAccrual(
    int Year,
    YearIncome Income,
    IReadOnlyList<MonthAccrual> Months,
    IReadOnlyList<QuarterAccrual> Quarters,
    LimitCrossing? LimitCrossing,
    LimitCrossing? StoppedAtYearEnd,
    EsvAnnex? EsvAnnex,
    IReadOnlyList<EngineWarning> Warnings)
{
    public bool InGroup3(int quarter) => Quarters.Any(accrual => accrual.Group3 && accrual.Income.Quarter == quarter);

    /// <summary>
    /// Whether the quarter has an accrual of <paramref name="kind"/>: ESV for every accrued quarter, the
    /// single tax and the levy for a group 3 one only.
    /// </summary>
    public bool Accrues(PaymentKind kind, int quarter) =>
        kind == PaymentKind.Esv
            ? Quarters.Any(accrual => accrual.Income.Quarter == quarter)
            : InGroup3(quarter);

    /// <summary>The quarter has to be accrued.</summary>
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
        var esvByMonth = EsvByMonth(year, config, settings);
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

            if (EndsBeforeGroup3(year, quarter, settings))
            {
                foreach (var month in income.Months.Skip(3 * quarter - 3).Take(3))
                {
                    months.Add(new MonthAccrual(month.Month, month.IncomeKop, 0, 0, esvByMonth[month.Month - 1].EsvKop));
                }

                quarters.Add(new QuarterAccrual(
                    quarterIncome with { CumulativeIncomeKop = cumulativeIncomeKop },
                    0,
                    accruedSingleTaxKop,
                    0,
                    0,
                    0,
                    accruedMilitaryLevyKop,
                    esvByMonth[(3 * quarter - 3)..(3 * quarter)].Sum(month => month.EsvKop),
                    Group3: false));
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
                    esvByMonth[month.Month - 1].EsvKop));
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
                esvByMonth[(3 * quarter - 3)..(3 * quarter)].Sum(month => month.EsvKop)));

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
            EsvAnnexOf(year, quarters, crossing, esvByMonth, settings),
            warnings);
    }

    private static EsvAnnex? EsvAnnexOf(
        int year,
        List<QuarterAccrual> quarters,
        LimitCrossing? crossing,
        EsvMonth[] esvByMonth,
        FopSettingsInput settings)
    {
        var months = quarters
            .SelectMany(quarter => esvByMonth[(3 * quarter.Income.Quarter - 3)..(3 * quarter.Income.Quarter)])
            .Where(month => month.BaseKop > 0)
            .ToArray();
        if (months.Length == 0
            || settings.FopRegistrationDate is not { } registrationDate
            || quarters.LastOrDefault(quarter => quarter.Group3
                && new DateOnly(year, 3 * quarter.Income.Quarter, 1).AddMonths(1).AddDays(-1) >= registrationDate) is not { } carrier)
        {
            return null;
        }

        var first = quarters[0].Income.Quarter;
        var last = carrier.Income.Quarter;
        var firstDay = new DateOnly(year, 3 * first - 2, 1);
        return new EsvAnnex(
            year,
            last,
            registrationDate > firstDay ? registrationDate : firstDay,
            new DateOnly(year, 3 * last, 1).AddMonths(1).AddDays(-1),
            months,
            crossing?.Quarter == last);
    }

    /// <summary>
    /// A quarter of the FOP that is over before group 3 starts is on the general system (Tax Code
    /// 298.1.4), whose taxes this engine does not compute. A quarter over before registration keeps its
    /// empty group 3 accrual, as Rule 8 has it.
    /// </summary>
    private static bool EndsBeforeGroup3(int year, int quarter, FopSettingsInput settings)
    {
        var quarterEnd = new DateOnly(year, 3 * quarter, 1).AddMonths(1).AddDays(-1);
        return settings is { FopRegistrationDate: { } registered, Group3Start: { } start }
            && quarterEnd >= registered
            && quarterEnd < start;
    }

    /// <summary>
    /// Rule 4: the single tax rate up to the limit and the excess rate above it, each rounded once. A
    /// month and a quarter both split this one function of the period's income so far, so they add up to
    /// the kopeck.
    /// </summary>
    private static long SingleTaxOn(long cumulativeIncomeKop, TaxYearConfigInput config) =>
        Money.ApplyBp(Math.Min(cumulativeIncomeKop, config.IncomeLimitKop), config.SingleTaxRateBp)
        + Money.ApplyBp(Math.Max(cumulativeIncomeKop - config.IncomeLimitKop, 0), config.ExcessRateBp);

    private static EsvMonth[] EsvByMonth(
        int year,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        var months = new EsvMonth[12];
        for (var month = 1; month <= 12; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var baseKop = 0L;
            if (!settings.EsvExempt && settings.FopRegistrationDate is { } registrationDate && monthEnd >= registrationDate)
            {
                baseKop = monthStart >= registrationDate
                    ? config.MinWageKop
                    : RegistrationMonthBaseKop(config.MinWageKop, registrationDate, monthEnd, settings);
            }

            months[month - 1] = new EsvMonth(month, baseKop, config.EsvRateBp);
        }

        return months;
    }

    private static long RegistrationMonthBaseKop(
        long minWageKop,
        DateOnly registrationDate,
        DateOnly monthEnd,
        FopSettingsInput settings) =>
        settings.EsvRegistrationMonthPolicy switch
        {
            EsvRegistrationMonthPolicy.FullMonth => minWageKop,
            EsvRegistrationMonthPolicy.Prorated => Money.Prorate(
                minWageKop, monthEnd.Day - registrationDate.Day + 1, monthEnd.Day),
            var policy => throw new ArgumentOutOfRangeException(
                nameof(settings), policy, "Unknown ESV registration-month policy."),
        };
}
