using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class AccrualsTests
{
    private const long EsvMonthKop = 190_234;

    [Theory]
    [InlineData(1, 333_333, 333_333, 16_667, 16_667, 3_333, 3_333, 570_702, 590_702)]
    [InlineData(2, 333_333, 666_666, 16_666, 33_333, 3_334, 6_667, 570_702, 590_702)]
    [InlineData(3, 0, 666_666, 0, 33_333, 0, 6_667, 570_702, 570_702)]
    [InlineData(4, 1_000_000, 1_666_666, 50_000, 83_333, 10_000, 16_667, 570_702, 630_702)]
    public void A_quarter_accrues_the_cumulative_tax_minus_what_earlier_quarters_accrued(
        int quarter,
        long incomeKop,
        long cumulativeIncomeKop,
        long singleTaxKop,
        long cumulativeSingleTaxKop,
        long militaryLevyKop,
        long cumulativeMilitaryLevyKop,
        long esvKop,
        long totalKop)
    {
        var actual = Accruals.ForYear(2026, RoundingYear, Config2026, RegisteredIn2025)
            .Quarters[quarter - 1];

        Assert.Equal(
            (incomeKop, cumulativeIncomeKop, singleTaxKop, cumulativeSingleTaxKop, militaryLevyKop,
                cumulativeMilitaryLevyKop, esvKop, totalKop),
            (actual.Income.IncomeKop, actual.Income.CumulativeIncomeKop, actual.SingleTaxKop,
                actual.CumulativeSingleTaxKop, actual.MilitaryLevyKop,
                actual.CumulativeMilitaryLevyKop, actual.EsvKop, actual.TotalKop));
    }

    [Theory]
    [InlineData("333333 333333 333333 333333 333333 333333 333333 333333 333333 333333 333333 333333")]
    [InlineData("1000000 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("500000 -800000 0 111111 0 0 0 0 0 0 0 77777")]
    [InlineData("0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("-100001 0 0 0 0 0 0 0 0 0 0 0")]
    public void The_four_quarterly_accruals_add_up_to_the_cumulative_accrual_for_the_year(
        string monthlyIncomeKop)
    {
        var actual = Accruals.ForYear(2026, ByMonth(monthlyIncomeKop), Config2026, RegisteredIn2025);

        Assert.Equal(
            actual.Quarters[^1].CumulativeSingleTaxKop,
            actual.Quarters.Sum(quarter => quarter.SingleTaxKop));
        Assert.Equal(
            actual.Quarters[^1].CumulativeMilitaryLevyKop,
            actual.Quarters.Sum(quarter => quarter.MilitaryLevyKop));
    }

    [Theory]
    [InlineData(500, 100, 864_700, 2_200, 50_000, 10_000, 570_702)]
    [InlineData(300, 250, 1_000_000, 1_000, 30_000, 25_000, 300_000)]
    [InlineData(1_500, 50, 700_000, 2_000, 150_000, 5_000, 420_000)]
    public void Every_rate_and_the_minimum_wage_come_from_the_year_config(
        int singleTaxRateBp,
        int militaryLevyRateBp,
        long minWageKop,
        int esvRateBp,
        long expectedSingleTaxKop,
        long expectedMilitaryLevyKop,
        long expectedEsvKop)
    {
        var config = Config2026 with
        {
            SingleTaxRateBp = singleTaxRateBp,
            MilitaryLevyRateBp = militaryLevyRateBp,
            MinWageKop = minWageKop,
            EsvRateBp = esvRateBp,
        };

        var actual = Accruals.ForYear(
            2026,
            [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)],
            config,
            RegisteredIn2025).Quarters[0];

        Assert.Equal(
            (expectedSingleTaxKop, expectedMilitaryLevyKop, expectedEsvKop),
            (actual.SingleTaxKop, actual.MilitaryLevyKop, actual.EsvKop));
    }

    [Theory]
    [InlineData("2026-01-01", 3)]
    [InlineData("2026-01-31", 3)]
    [InlineData("2026-02-15", 2)]
    [InlineData("2026-03-10", 1)]
    [InlineData("2026-04-01", 0)]
    public void Esv_starts_in_the_month_of_registration(string registrationDate, int activeMonths)
    {
        var actual = Accruals.ForYear(2026, [], Config2026, Settings(Date(registrationDate)));

        Assert.Equal(activeMonths * EsvMonthKop, actual.Quarters[0].EsvKop);
    }

    [Theory]
    [InlineData("2026-02-15", EsvRegistrationMonthPolicy.FullMonth, 380_468)]
    [InlineData("2026-02-15", EsvRegistrationMonthPolicy.Prorated, 285_351)]
    [InlineData("2026-03-10", EsvRegistrationMonthPolicy.FullMonth, 190_234)]
    [InlineData("2026-03-10", EsvRegistrationMonthPolicy.Prorated, 135_005)]
    [InlineData("2026-01-31", EsvRegistrationMonthPolicy.FullMonth, 570_702)]
    [InlineData("2026-01-31", EsvRegistrationMonthPolicy.Prorated, 386_605)]
    public void The_month_of_registration_costs_a_full_month_or_its_active_days(
        string registrationDate,
        EsvRegistrationMonthPolicy policy,
        long expectedEsvKop)
    {
        var settings = Settings(Date(registrationDate)) with
        {
            EsvRegistrationMonthPolicy = policy,
        };

        var actual = Accruals.ForYear(2026, [], Config2026, settings);

        Assert.Equal(expectedEsvKop, actual.Quarters[0].EsvKop);
    }

    [Fact]
    public void The_esv_exemption_zeroes_esv_and_leaves_the_single_tax_and_the_levy_untouched()
    {
        var exempt = Accruals.ForYear(
            2026, RoundingYear, Config2026, RegisteredIn2025 with { EsvExempt = true });
        var liable = Accruals.ForYear(2026, RoundingYear, Config2026, RegisteredIn2025);

        Assert.Equal(new long[] { 0, 0, 0, 0 }, exempt.Quarters.Select(quarter => quarter.EsvKop));
        Assert.Equal(
            liable.Quarters.Select(quarter => (quarter.SingleTaxKop, quarter.MilitaryLevyKop)),
            exempt.Quarters.Select(quarter => (quarter.SingleTaxKop, quarter.MilitaryLevyKop)));
    }

    [Fact]
    public void A_year_that_ends_before_the_registration_date_accrues_nothing()
    {
        var actual = Accruals.ForYear(
            2026, RoundingYear, Config2026, Settings(Date("2027-03-01")));

        Assert.Equal(
            new long[] { 0, 0, 0, 0 }, actual.Quarters.Select(quarter => quarter.TotalKop));
        Assert.NotEmpty(actual.Warnings);
    }

    [Fact]
    public void Without_a_registration_date_nothing_accrues_and_the_warning_reaches_the_year()
    {
        var actual = Accruals.ForYear(
            2026, RoundingYear, Config2026, Settings(registrationDate: null));

        Assert.Equal(
            new long[] { 0, 0, 0, 0 }, actual.Quarters.Select(quarter => quarter.TotalKop));
        Assert.Equal([new EngineWarning.FopRegistrationDateNotSet()], actual.Warnings);
    }

    [Fact]
    public void A_year_after_the_registration_year_accrues_twelve_months_of_esv()
    {
        var actual = Accruals.ForYear(
            2026, [], Config2026, Settings(Date("2025-06-10")));

        Assert.Equal(12 * EsvMonthKop, actual.Quarters.Sum(quarter => quarter.EsvKop));
    }

    [Fact]
    public void A_year_other_than_2026_still_computes_esv_by_its_own_calendar()
    {
        var actual = Accruals.ForYear(2027, [], Config2026, Settings(Date("2027-03-10")));

        Assert.Equal(10 * EsvMonthKop, actual.Quarters.Sum(quarter => quarter.EsvKop));
    }

    [Fact]
    public void Registration_on_the_last_day_of_a_leap_february_prorates_by_its_true_length()
    {
        var settings = Settings(Date("2028-02-29")) with
        {
            EsvRegistrationMonthPolicy = EsvRegistrationMonthPolicy.Prorated,
        };

        var actual = Accruals.ForYear(2028, [], Config2026, settings);

        Assert.Equal(6_560 + EsvMonthKop, actual.Quarters[0].EsvKop);
    }

    [Fact]
    public void Registration_on_the_second_of_the_month_is_prorated_not_charged_a_full_month()
    {
        var settings = Settings(Date("2026-02-02")) with
        {
            EsvRegistrationMonthPolicy = EsvRegistrationMonthPolicy.Prorated,
        };

        var actual = Accruals.ForYear(2026, [], Config2026, settings);

        Assert.Equal(183_440 + EsvMonthKop, actual.Quarters[0].EsvKop);
    }

    [Fact]
    public void A_refund_that_shrinks_the_cumulative_income_makes_the_quarter_tax_negative()
    {
        var actual = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 1_000_000),
                new TransactionInput.RefundToClient(Date("2026-05-10"), 600_000),
            ],
            Config2026,
            RegisteredIn2025);

        Assert.Equal(
            (50_000L, -30_000L, 20_000L),
            (actual.Quarters[0].SingleTaxKop,
                actual.Quarters[1].SingleTaxKop,
                actual.Quarters[1].CumulativeSingleTaxKop));
        Assert.Equal(-600_000, actual.Months[4].IncomeKop);
    }

    [Fact]
    public void Operations_before_the_registration_date_are_warned_about_and_left_out_of_the_tax()
    {
        var actual = Accruals.ForYear(
            2026,
            [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)],
            Config2026,
            Settings(Date("2026-03-15")));

        Assert.Equal(
            [new EngineWarning.OperationBeforeRegistration(Date("2026-02-10"), Date("2026-03-15"))],
            actual.Warnings);
        Assert.Equal(0, actual.Quarters[0].SingleTaxKop);
    }

    [Fact]
    public void A_negative_cumulative_tax_from_a_refund_of_an_earlier_periods_receipt_is_warned_about()
    {
        var actual = Accruals.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2025-12-20"), 10_000_000),
                new TransactionInput.RefundToClient(Date("2026-04-15"), 10_000_000),
            ],
            Config2026,
            RegisteredIn2025);

        Assert.Equal(-500_000, actual.Quarters[1].CumulativeSingleTaxKop);
        Assert.Equal(
            [
                new EngineWarning.NegativeCumulativeTax(2),
                new EngineWarning.NegativeCumulativeTax(3),
                new EngineWarning.NegativeCumulativeTax(4),
            ],
            actual.Warnings);
    }

    [Fact]
    public void An_unknown_registration_month_policy_is_rejected()
    {
        var settings = Settings(Date("2026-02-15")) with
        {
            EsvRegistrationMonthPolicy = (EsvRegistrationMonthPolicy)7,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Accruals.ForYear(2026, [], Config2026, settings));
    }

    private static readonly TaxYearConfigInput Config2026 = new(
        MinWageKop: 864_700,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 100,
        EsvRateBp: 2_200,
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: []);

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static readonly TransactionInput[] RoundingYear =
    [
        new TransactionInput.Income(Date("2026-02-10"), 333_333),
        new TransactionInput.Income(Date("2026-05-10"), 333_333),
        new TransactionInput.Income(Date("2026-11-10"), 1_000_000),
    ];

    private static TransactionInput[] ByMonth(string monthlyIncomeKop) =>
        [.. monthlyIncomeKop.Split(' ')
            .Select((amount, index) => (
                amountKop: long.Parse(amount, CultureInfo.InvariantCulture),
                valueDate: new DateOnly(2026, index + 1, 10)))
            .Where(month => month.amountKop != 0)
            .Select(month => month.amountKop > 0
                ? new TransactionInput.Income(month.valueDate, month.amountKop)
                : (TransactionInput)new TransactionInput.RefundToClient(
                    month.valueDate, -month.amountKop))];

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
