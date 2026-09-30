using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class LimitCrossingTests
{
    private const long LimitKop = 1_009_104_900;
    private const long EsvQuarterKop = 570_702;

    private static readonly TransactionInput[] CrossedMidQ3 =
    [
        Income("2026-02-10", 400_000_000),
        Income("2026-05-10", 400_000_000),
        Income("2026-08-10", 150_000_000),
        Income("2026-09-10", 100_000_000),
        Income("2026-11-10", 10_000_000),
    ];

    [Theory]
    [InlineData(1, 400_000_000, 20_000_000, 20_000_000, 0, 0, 4_000_000)]
    [InlineData(2, 800_000_000, 20_000_000, 40_000_000, 0, 0, 4_000_000)]
    [InlineData(3, 1_050_000_000, 16_589_510, 56_589_510, 40_895_100, 6_134_265, 2_500_000)]
    public void The_crossing_quarter_accrues_the_excess_rate_on_the_income_over_the_limit_as_single_tax(
        int quarter,
        long cumulativeIncomeKop,
        long singleTaxKop,
        long cumulativeSingleTaxKop,
        long excessIncomeKop,
        long excessTaxKop,
        long militaryLevyKop)
    {
        var actual = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025).Quarters[quarter - 1];

        Assert.Equal(
            (cumulativeIncomeKop, singleTaxKop, cumulativeSingleTaxKop, excessIncomeKop, excessTaxKop, militaryLevyKop),
            (actual.Income.CumulativeIncomeKop, actual.SingleTaxKop, actual.CumulativeSingleTaxKop,
                actual.CumulativeExcessIncomeKop, actual.CumulativeExcessTaxKop, actual.MilitaryLevyKop));
    }

    [Fact]
    public void Quarters_after_the_crossing_are_not_accrued_and_say_where_the_switch_starts()
    {
        var year = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025);

        Assert.Equal(3, year.Quarters.Count);
        Assert.Equal(9, year.Months.Count);
        Assert.Equal(new LimitCrossing(2026, 3), year.LimitCrossing);
        Assert.Equal((2026, 4), (year.LimitCrossing!.SwitchFromYear, year.LimitCrossing.SwitchFromQuarter));
        Assert.Equal([true, true, true, false], new[] { 1, 2, 3, 4 }.Select(year.InGroup3));
        Assert.Equal(10_000_000, year.Income.Quarters[3].IncomeKop);
        Assert.Equal(1_060_000_000, year.Income.TotalIncomeKop);
    }

    [Fact]
    public void A_crossing_in_q4_switches_from_the_next_years_first_quarter()
    {
        var year = Accruals.ForYear(2026, [Income("2026-12-30", LimitKop + 1)], Config2026, RegisteredIn2025);

        Assert.Equal(4, year.Quarters.Count);
        Assert.Equal((2027, 1), (year.LimitCrossing!.SwitchFromYear, year.LimitCrossing.SwitchFromQuarter));
    }

    [Theory]
    [InlineData("2026-03-31", 1)]
    [InlineData("2026-04-01", 2)]
    public void A_receipt_on_either_side_of_a_quarter_boundary_crosses_in_its_own_quarter(string date, int quarter)
    {
        var year = Accruals.ForYear(2026, [Income(date, LimitKop + 100)], Config2026, RegisteredIn2025);

        Assert.Equal(quarter, year.LimitCrossing!.Quarter);
        Assert.Equal((100L, 15L), (year.Quarters[^1].CumulativeExcessIncomeKop, year.Quarters[^1].CumulativeExcessTaxKop));
    }

    [Fact]
    public void Income_exactly_at_the_limit_has_no_excess_and_stays_in_group_3()
    {
        var year = Accruals.ForYear(2026, [Income("2026-05-10", LimitKop)], Config2026, RegisteredIn2025);

        Assert.Null(year.LimitCrossing);
        Assert.Equal(4, year.Quarters.Count);
        Assert.Equal((0L, 0L, 50_455_245L), (year.Quarters[1].CumulativeExcessIncomeKop,
            year.Quarters[1].CumulativeExcessTaxKop, year.Quarters[1].CumulativeSingleTaxKop));
    }

    [Fact]
    public void A_refund_in_the_same_quarter_that_ends_it_under_the_limit_means_no_crossing()
    {
        var year = Accruals.ForYear(
            2026,
            [Income("2026-09-10", 1_050_000_000), Refund("2026-09-20", 50_000_000)],
            Config2026,
            RegisteredIn2025);

        Assert.Null(year.LimitCrossing);
        Assert.Equal(4, year.Quarters.Count);
        Assert.Equal(50_000_000, year.Quarters[2].CumulativeSingleTaxKop);
    }

    [Fact]
    public void A_refund_after_the_crossing_quarter_undoes_neither_the_crossing_nor_its_figures()
    {
        var crossed = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025);
        var refunded = Accruals.ForYear(
            2026, [.. CrossedMidQ3, Refund("2026-10-10", 100_000_000)], Config2026, RegisteredIn2025);

        Assert.Equal(crossed.LimitCrossing, refunded.LimitCrossing);
        Assert.Equal(crossed.Quarters, refunded.Quarters);
    }

    [Fact]
    public void The_months_of_the_crossing_quarter_add_up_to_its_excess_inclusive_accrual()
    {
        var year = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025);

        Assert.Equal([0L, 7_500_000L, 9_089_510L], year.Months.Skip(6).Select(month => month.SingleTaxKop));
        Assert.Equal(year.Quarters[2].SingleTaxKop, year.Months.Skip(6).Sum(month => month.SingleTaxKop));
    }

    [Fact]
    public void The_crossing_quarters_declaration_splits_income_and_tax_at_the_limit()
    {
        var year = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025);

        var actual = Declaration.ForQuarter(year, 3);

        Assert.Equal(
            (LimitKop, 40_895_100L, 1_050_000_000L, 50_455_245L, 6_134_265L, 56_589_510L, 40_000_000L, 16_589_510L,
                10_500_000L, 8_000_000L, 2_500_000L),
            (actual.IncomeKop, actual.ExcessIncomeKop, actual.TotalIncomeKop, actual.SingleTaxKop, actual.ExcessTaxKop,
                actual.TotalSingleTaxKop, actual.PreviousSingleTaxKop, actual.SingleTaxPayableKop,
                actual.MilitaryLevyKop, actual.PreviousMilitaryLevyKop, actual.MilitaryLevyPayableKop));
    }

    [Fact]
    public void A_quarter_below_the_limit_leaves_lines_07_and_09_at_zero()
    {
        var actual = Declaration.ForQuarter(Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025), 2);

        Assert.Equal((800_000_000L, 0L, 800_000_000L, 40_000_000L, 0L, 40_000_000L),
            (actual.IncomeKop, actual.ExcessIncomeKop, actual.TotalIncomeKop, actual.SingleTaxKop, actual.ExcessTaxKop,
                actual.TotalSingleTaxKop));
    }

    [Fact]
    public void A_quarter_after_the_crossing_has_no_group_3_declaration()
    {
        var year = Accruals.ForYear(2026, CrossedMidQ3, Config2026, RegisteredIn2025);

        Assert.Throws<ArgumentOutOfRangeException>(() => Declaration.ForQuarter(year, 4));
    }

    [Fact]
    public void The_excess_is_owed_as_single_tax_with_the_crossing_quarter_and_nothing_after_it()
    {
        var (_, ledger) = Ledger(RegisteredInQ3, Date("2026-10-05"));

        Assert.Equal(
            [(3, 56_589_510L)],
            ledger.SingleTax.Obligations.Where(o => o.AccruedKop != 0).Select(o => (o.Quarter, o.RemainingKop)));
        Assert.DoesNotContain(ledger.Esv.Obligations, obligation => obligation.Quarter == 4);
        Assert.Equal(3, ledger.MilitaryLevy.Obligations.Count);
    }

    [Fact]
    public void The_next_step_includes_the_excess_in_the_single_tax()
    {
        var (_, ledger) = Ledger(RegisteredInQ3, Date("2026-10-05"));

        var step = Assert.IsType<NextStep.Pay>(NextStep.Find(ledger, Date("2026-07-01"), Date("2026-10-05"), null));

        Assert.Equal([(PaymentKind.Esv, EsvQuarterKop)], step.Now.Select(debt => (debt.Kind, debt.AmountKop)));
        Assert.Equal(
            [(PaymentKind.SingleTax, 56_589_510L, 3), (PaymentKind.MilitaryLevy, 10_500_000L, 3)],
            step.Later.Select(debt => (debt.Kind, debt.AmountKop, debt.ToQuarter)));
    }

    [Fact]
    public void Monthly_advances_stop_with_the_crossing_quarter_and_carry_its_excess()
    {
        var (year, ledger) = Ledger(RegisteredInQ3, Date("2026-10-05"));

        var advances = MonthlyAdvances.ForYear(year, ledger, 15);

        Assert.Equal(9, advances.Count);
        Assert.Equal(
            [45_000_000L, 0L, 11_589_510L],
            advances.Skip(6).Select(month => month.SingleTax.RemainingKop));
        var step = Assert.IsType<NextStep.Pay>(
            NextStep.Find(ledger, Date("2026-07-01"), Date("2026-10-05"), advances));
        Assert.Contains(step.Now, debt => debt is { Kind: PaymentKind.SingleTax, AmountKop: 56_589_510, AdvanceMonth: 9 });
    }

    [Fact]
    public void The_reserve_needs_the_excess_and_nothing_for_the_quarters_after_the_crossing()
    {
        var (year, ledger) = Ledger(RegisteredInQ3, Date("2026-12-01"));

        var need = TaxReserve.Needed(ledger, [new LedgerYear(year, Config2026)], Date("2026-12-01"));

        Assert.Equal(
            [
                new ReserveDue(Date("2026-10-19"), ObligationStatus.Overdue, 0, 0, EsvQuarterKop),
                new ReserveDue(Date("2026-11-19"), ObligationStatus.Overdue, 56_589_510, 10_500_000, 0),
            ],
            need.Dues);
    }

    private static readonly TransactionInput[] RegisteredInQ3 =
    [
        Income("2026-07-10", 900_000_000),
        Income("2026-09-10", 150_000_000),
        Income("2026-10-10", 5_000_000),
    ];

    private static (YearAccrual Year, PaymentLedger Ledger) Ledger(TransactionInput[] transactions, DateOnly today)
    {
        var settings = Settings(Date("2026-07-01"));
        var year = Accruals.ForYear(2026, transactions, Config2026, settings);
        return (year, Balances.ForYears([new LedgerYear(year, Config2026)], settings, [], today));
    }

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

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static TransactionInput Income(string date, long amountKop) =>
        new TransactionInput.Income(Date(date), amountKop);

    private static TransactionInput Refund(string date, long amountKop) =>
        new TransactionInput.RefundToClient(Date(date), amountKop);

    private static FopSettingsInput Settings(DateOnly registrationDate) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
