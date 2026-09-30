using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class DeclarationTests
{
    [Theory]
    [InlineData(1, 10_000_000, 500_000, 0, 500_000, 100_000, 0, 100_000)]
    [InlineData(2, 25_000_000, 1_250_000, 500_000, 750_000, 250_000, 100_000, 150_000)]
    [InlineData(3, 30_000_000, 1_500_000, 1_250_000, 250_000, 300_000, 250_000, 50_000)]
    [InlineData(4, 50_000_000, 2_500_000, 1_500_000, 1_000_000, 500_000, 300_000, 200_000)]
    public void Each_quarter_declares_the_cumulative_figures_and_the_difference_from_the_previous_period(
        int quarter,
        long line06,
        long line11,
        long line13,
        long line141,
        long line23,
        long line24,
        long line25)
    {
        var year = Accruals.ForYear(2026, EvenYear, Config2026, RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, quarter);

        Assert.Equal(
            (2026, quarter, line06, line11, line13, line141, line23, line24, line25),
            (actual.Year, actual.Quarter, actual.IncomeKop, actual.SingleTaxKop, actual.PreviousSingleTaxKop,
                actual.SingleTaxPayableKop, actual.MilitaryLevyKop, actual.PreviousMilitaryLevyKop,
                actual.MilitaryLevyPayableKop));
    }

    [Theory]
    [InlineData(1, 12_345_678, 617_284, 0, 617_284, 123_457, 0, 123_457)]
    [InlineData(2, 22_222_221, 1_111_111, 617_284, 493_827, 222_222, 123_457, 98_765)]
    public void The_2026_reference_example_of_rule_14_rounds_each_cumulative_line_once(
        int quarter,
        long line06,
        long line11,
        long line13,
        long line141,
        long line23,
        long line24,
        long line25)
    {
        var year = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-01-20"), 12_345_678),
                new TransactionInput.Income(Date("2026-04-15"), 9_876_543),
            ],
            Config2026,
            RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, quarter);

        Assert.Equal(
            (line06, line11, line13, line141, line23, line24, line25),
            (actual.IncomeKop, actual.SingleTaxKop, actual.PreviousSingleTaxKop, actual.SingleTaxPayableKop,
                actual.MilitaryLevyKop, actual.PreviousMilitaryLevyKop, actual.MilitaryLevyPayableKop));
    }

    [Fact]
    public void A_refund_that_shrinks_the_cumulative_income_leaves_lines_14_1_and_25_negative()
    {
        var year = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 10_000_000),
                new TransactionInput.RefundToClient(Date("2026-05-10"), 4_000_000),
            ],
            Config2026,
            RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, 2);

        Assert.Equal(
            (6_000_000L, 300_000L, 500_000L, -200_000L, 60_000L, 100_000L, -40_000L),
            (actual.IncomeKop, actual.SingleTaxKop, actual.PreviousSingleTaxKop, actual.SingleTaxPayableKop,
                actual.MilitaryLevyKop, actual.PreviousMilitaryLevyKop, actual.MilitaryLevyPayableKop));
    }

    [Fact]
    public void A_first_year_registered_in_may_declares_nothing_for_q1_and_leaves_out_income_before_registration()
    {
        var year = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-03-10"), 1_000_000),
                new TransactionInput.Income(Date("2026-06-10"), 2_000_000),
            ],
            Config2026,
            Settings(Date("2026-05-15")));

        var first = Declaration.ForQuarter(year, 1);
        var second = Declaration.ForQuarter(year, 2);

        Assert.Equal(
            (0L, 0L, 0L, 0L, 0L, 0L, 0L),
            (first.IncomeKop, first.SingleTaxKop, first.PreviousSingleTaxKop, first.SingleTaxPayableKop,
                first.MilitaryLevyKop, first.PreviousMilitaryLevyKop, first.MilitaryLevyPayableKop));
        Assert.Equal(
            (2_000_000L, 100_000L, 0L, 100_000L, 20_000L, 0L, 20_000L),
            (second.IncomeKop, second.SingleTaxKop, second.PreviousSingleTaxKop, second.SingleTaxPayableKop,
                second.MilitaryLevyKop, second.PreviousMilitaryLevyKop, second.MilitaryLevyPayableKop));
        Assert.Equal(8 * EsvMonthKop, Declaration.ForQuarter(year, 4).EsvKop);
    }

    [Theory]
    [InlineData(2, 1, 100_000)]
    [InlineData(3, 2, 250_000)]
    [InlineData(4, 3, 300_000)]
    public void Line_24_is_line_23_of_the_previous_quarter(int quarter, int previousQuarter, long line24)
    {
        var year = Accruals.ForYear(2026, EvenYear, Config2026, RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, quarter);

        Assert.Equal(line24, actual.PreviousMilitaryLevyKop);
        Assert.Equal(Declaration.ForQuarter(year, previousQuarter).MilitaryLevyKop, actual.PreviousMilitaryLevyKop);
    }

    [Theory]
    [InlineData(100, 200_000, 100_000, 100_000)]
    [InlineData(150, 300_000, 150_000, 150_000)]
    public void The_levy_lines_use_the_rate_of_the_declared_year(
        int militaryLevyRateBp, long line23, long line24, long line25)
    {
        var year = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 10_000_000),
                new TransactionInput.Income(Date("2026-05-10"), 10_000_000),
            ],
            Config2026 with { MilitaryLevyRateBp = militaryLevyRateBp },
            RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, 2);

        Assert.Equal(
            (line23, line24, line25),
            (actual.MilitaryLevyKop, actual.PreviousMilitaryLevyKop, actual.MilitaryLevyPayableKop));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Only_the_annual_declaration_carries_line_21(int quarter)
    {
        var year = Accruals.ForYear(2026, EvenYear, Config2026, RegisteredIn2025);

        Assert.Null(Declaration.ForQuarter(year, quarter).EsvKop);
    }

    [Fact]
    public void Line_21_of_a_full_year_is_twelve_months_of_esv()
    {
        var year = Accruals.ForYear(2026, EvenYear, Config2026, RegisteredIn2025);

        Assert.Equal(2_282_808, Declaration.ForQuarter(year, 4).EsvKop);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_quarter_outside_1_to_4_is_refused(int quarter)
    {
        var year = Accruals.ForYear(2026, EvenYear, Config2026, RegisteredIn2025);

        Assert.Throws<ArgumentOutOfRangeException>(() => Declaration.ForQuarter(year, quarter));
    }

    private const long EsvMonthKop = 190_234;

    private static readonly TaxYearConfigInput Config2026 = new(
        MinWageKop: 864_700,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 100,
        EsvRateBp: 2_200,
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: [],
        IncomeLimitKop: 1_009_104_900,
        ExcessRateBp: 1_500,
        LimitWarnThresholdsPct: [85, 100]);

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static readonly TransactionInput[] EvenYear =
    [
        new TransactionInput.Income(Date("2026-02-10"), 10_000_000),
        new TransactionInput.Income(Date("2026-05-10"), 15_000_000),
        new TransactionInput.Income(Date("2026-08-10"), 5_000_000),
        new TransactionInput.Income(Date("2026-11-10"), 20_000_000),
    ];

    private static FopSettingsInput Settings(DateOnly? registrationDate) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
