using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class LimitCrossingAcrossYearsTests
{
    private const long LimitKop = 1_009_104_900;

    private static readonly TransactionInput[] CrossedInQ4Of2026 = [Income("2026-11-10", LimitKop + 100)];

    private static readonly TransactionInput[] CrossedInQ2Of2026 =
    [
        Income("2026-02-10", 600_000_000),
        Income("2026-05-10", 600_000_000),
    ];

    private static readonly TransactionInput[] Income2027 =
    [
        Income("2027-02-10", 30_000_000),
        Income("2027-05-10", 20_000_000),
        Income("2027-08-10", 10_000_000),
    ];

    [Fact]
    public void A_q4_crossing_keeps_every_quarter_of_the_next_years_out_of_group_3()
    {
        var years = Accruals.ForYears(Years(CrossedInQ4Of2026, 2026, 2027, 2028), RegisteredIn2025);

        Assert.Equal(4, years[0].Quarters.Count);
        Assert.All(years.Skip(1), year =>
        {
            Assert.Empty(year.Quarters);
            Assert.Empty(year.Months);
            Assert.Equal(new LimitCrossing(2026, 4), year.LimitCrossing);
            Assert.Equal(new LimitCrossing(2026, 4), year.StoppedAtYearEnd);
        });
        Assert.Equal(30_000_000, years[1].Income.Quarters[0].IncomeKop);
    }

    [Fact]
    public void A_mid_year_crossing_keeps_the_next_year_out_of_group_3()
    {
        var years = Accruals.ForYears(Years(CrossedInQ2Of2026, 2026, 2027), RegisteredIn2025);

        Assert.Equal([1, 2], years[0].Quarters.Select(quarter => quarter.Income.Quarter));
        Assert.Empty(years[1].Quarters);
        Assert.Equal(new LimitCrossing(2026, 2), years[1].LimitCrossing);
        Assert.Equal((2026, 3), (years[1].LimitCrossing!.SwitchFromYear, years[1].LimitCrossing!.SwitchFromQuarter));
    }

    [Fact]
    public void A_year_missing_from_the_run_passes_the_stop_on()
    {
        var years = Accruals.ForYears(Years(CrossedInQ4Of2026, 2026, 2028), RegisteredIn2025);

        Assert.Empty(years[1].Quarters);
        Assert.Equal(new LimitCrossing(2026, 4), years[1].LimitCrossing);
    }

    [Fact]
    public void Back_on_group_3_resumes_from_its_quarter_with_the_period_starting_there()
    {
        var years = Accruals.ForYears(
            Years(CrossedInQ4Of2026, 2026, 2027, 2028), RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2027, 2) });
        var q2 = years[1].QuarterOf(2);
        var q3 = Declaration.ForQuarter(years[1], 3);

        Assert.Equal([2, 3, 4], years[1].Quarters.Select(quarter => quarter.Income.Quarter));
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12], years[1].Months.Select(month => month.Month));
        Assert.Equal((20_000_000L, 1_000_000L, 1_000_000L), (q2.Income.CumulativeIncomeKop, q2.SingleTaxKop, q2.CumulativeSingleTaxKop));
        Assert.Equal((30_000_000L, 1_000_000L, 500_000L), (q3.TotalIncomeKop, q3.PreviousSingleTaxKop, q3.SingleTaxPayableKop));
        Assert.Equal(new LimitCrossing(2026, 4), years[1].LimitCrossing);
        Assert.Null(years[1].StoppedAtYearEnd);
        Assert.Equal((4, (LimitCrossing?)null), (years[2].Quarters.Count, years[2].LimitCrossing));
    }

    [Fact]
    public void Back_on_group_3_from_the_switch_quarter_leaves_nothing_out()
    {
        var years = Accruals.ForYears(
            Years(CrossedInQ4Of2026, 2026, 2027), RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2027, 1) });

        Assert.Equal(4, years[1].Quarters.Count);
        Assert.Null(years[1].LimitCrossing);
        Assert.Equal(1_500_000, years[1].QuarterOf(1).SingleTaxKop);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 4)]
    public void Back_on_group_3_not_after_the_crossing_lifts_nothing(int year, int quarter)
    {
        var years = Accruals.ForYears(
            Years(CrossedInQ4Of2026, 2026, 2027), RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(year, quarter) });

        Assert.Empty(years[1].Quarters);
    }

    [Fact]
    public void Back_on_group_3_later_in_the_crossing_year_starts_a_new_period()
    {
        var years = Accruals.ForYears(
            Years([.. CrossedInQ2Of2026, Income("2026-08-10", 1_000_000), Income("2026-11-10", 2_000_000)], 2026),
            RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2026, 4) });
        var q4 = Declaration.ForQuarter(years[0], 4);

        Assert.Equal([1, 2, 4], years[0].Quarters.Select(quarter => quarter.Income.Quarter));
        Assert.Equal((2_000_000L, 0L, 100_000L), (q4.TotalIncomeKop, q4.PreviousSingleTaxKop, q4.SingleTaxPayableKop));
        Assert.Equal(new LimitCrossing(2026, 2), years[0].LimitCrossing);
    }

    [Fact]
    public void The_ledger_owes_nothing_for_the_years_after_the_crossing()
    {
        var years = Accruals.ForYears(Years(CrossedInQ4Of2026, 2026, 2027), RegisteredIn2025);

        var ledger = Balances.ForYears(
            [.. years.Select(year => new LedgerYear(year, Config2026))], RegisteredIn2025, [], Date("2027-12-31"));

        Assert.All(
            new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }.SelectMany(kind => kind.Obligations),
            obligation => Assert.Equal(2026, obligation.Year));
    }

    private static AccrualYearInput[] Years(TransactionInput[] transactions, params int[] years) =>
    [
        .. years.Select(year => new AccrualYearInput(
            year,
            [.. transactions.Concat(Income2027).Where(transaction => transaction.ValueDate.Year == year)],
            Config2026)),
    ];

    private static readonly TaxYearConfigInput Config2026 = new(
        MinWageKop: 864_700,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 100,
        EsvRateBp: 2_200,
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: [],
        IncomeLimitKop: LimitKop,
        ExcessRateBp: 1_500,
        LimitWarnThresholdsPct: [85, 100]);

    private static readonly FopSettingsInput RegisteredIn2025 = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: Date("2025-01-01"),
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static TransactionInput Income(string date, long amountKop) =>
        new TransactionInput.Income(Date(date), amountKop);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
