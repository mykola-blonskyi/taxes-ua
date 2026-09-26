using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class BalancesTests
{
    private const long EsvQuarterKop = 570_702;
    private const long EsvYearKop = 4 * EsvQuarterKop;

    [Theory]
    [InlineData(1, EsvQuarterKop, 2 * EsvQuarterKop, 0, -EsvQuarterKop)]
    [InlineData(2, EsvQuarterKop, 0, -EsvQuarterKop, 0)]
    [InlineData(3, EsvQuarterKop, 0, 0, EsvQuarterKop)]
    [InlineData(4, EsvQuarterKop, 0, EsvQuarterKop, 2 * EsvQuarterKop)]
    public void An_esv_overpayment_covers_the_next_quarters_esv(
        int quarter,
        long accruedKop,
        long paidKop,
        long openingBalanceKop,
        long balanceKop)
    {
        var actual = Balance(
            OneReceipt,
            Paid(PaymentKind.Esv, 2 * EsvQuarterKop, quarter: 1)).Esv.Quarters[quarter - 1];

        Assert.Equal(
            new QuarterBalance(quarter, accruedKop, paidKop, openingBalanceKop, balanceKop),
            actual);
    }

    [Fact]
    public void An_ep_overpayment_never_reduces_an_esv_or_a_military_levy_debt()
    {
        var actual = Balance(OneReceipt, Paid(PaymentKind.SingleTax, 5_000_000, quarter: 1));

        Assert.Equal(
            (50_000 - 5_000_000L, 10_000L, EsvYearKop),
            (actual.SingleTax.ClosingBalanceKop,
                actual.MilitaryLevy.ClosingBalanceKop,
                actual.Esv.ClosingBalanceKop));
    }

    [Fact]
    public void Paying_one_kind_the_whole_years_liability_of_all_three_leaves_the_other_two_owing()
    {
        var everythingKop = 50_000 + 10_000 + EsvYearKop;

        var actual = Balance(OneReceipt, Paid(PaymentKind.SingleTax, everythingKop, quarter: 1));

        Assert.Equal(
            (50_000 - everythingKop, 10_000L, EsvYearKop),
            (actual.SingleTax.ClosingBalanceKop,
                actual.MilitaryLevy.ClosingBalanceKop,
                actual.Esv.ClosingBalanceKop));
    }

    [Theory]
    [InlineData(PaymentKind.SingleTax)]
    [InlineData(PaymentKind.MilitaryLevy)]
    [InlineData(PaymentKind.Esv)]
    public void A_payment_of_one_kind_is_credited_to_that_kind_and_to_no_other(PaymentKind paidKind)
    {
        var balances = Balance(
            OneReceipt, Paid(paidKind, 1_000_000, quarter: 1), Paid(paidKind, 2_000_000, quarter: 3));

        foreach (var ledger in new[] { balances.SingleTax, balances.MilitaryLevy, balances.Esv })
        {
            Assert.Equal(
                ledger.Kind == paidKind
                    ? new long[] { 1_000_000, 0, 2_000_000, 0 }
                    : new long[] { 0, 0, 0, 0 },
                ledger.Quarters.Select(quarter => quarter.PaidKop));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(11, 4)]
    [InlineData(12, 4)]
    public void A_monthly_payment_credits_the_quarter_that_contains_the_month(int month, int quarter)
    {
        var payment = new BudgetPaymentInput(
            PaymentKind.Esv, 190_234, 2026, new PaymentPeriod.Monthly(month));

        var actual = Balance(OneReceipt, payment).Esv;

        Assert.Equal(
            [quarter == 1 ? 190_234 : 0, quarter == 2 ? 190_234 : 0, quarter == 3 ? 190_234 : 0,
                quarter == 4 ? 190_234 : 0],
            actual.Quarters.Select(quarter => quarter.PaidKop));
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    public void A_payment_recorded_against_another_year_settles_nothing_in_this_one(int periodYear)
    {
        var payment = new BudgetPaymentInput(
            PaymentKind.Esv, EsvQuarterKop, periodYear, new PaymentPeriod.Quarterly(1));

        var actual = Balance(OneReceipt, payment).Esv.Quarters[0];

        Assert.Equal((0L, EsvQuarterKop), (actual.PaidKop, actual.BalanceKop));
    }

    [Fact]
    public void A_payment_against_a_quarter_that_accrued_nothing_carries_forward_as_an_overpayment()
    {
        var actual = Balance(
            OneReceipt,
            Paid(PaymentKind.SingleTax, 50_000, quarter: 1),
            Paid(PaymentKind.SingleTax, 30_000, quarter: 3)).SingleTax;

        Assert.Equal(
            [0L, 0L, -30_000L, -30_000L],
            actual.Quarters.Select(quarter => quarter.BalanceKop));
    }

    [Fact]
    public void An_overpayment_larger_than_the_whole_years_liability_stays_on_the_closing_balance()
    {
        var actual = Balance(OneReceipt, Paid(PaymentKind.Esv, 5_000_000, quarter: 1)).Esv;

        Assert.Equal(
            [
                EsvQuarterKop - 5_000_000,
                2 * EsvQuarterKop - 5_000_000,
                3 * EsvQuarterKop - 5_000_000,
                EsvYearKop - 5_000_000,
            ],
            actual.Quarters.Select(quarter => quarter.BalanceKop));
        Assert.Equal(EsvYearKop - 5_000_000, actual.ClosingBalanceKop);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_quarter_balance_is_what_it_opened_with_plus_what_accrued_minus_what_was_paid(
        int scenario)
    {
        var balances = Scenarios[scenario];

        foreach (var kind in new[] { balances.SingleTax, balances.MilitaryLevy, balances.Esv })
        {
            var openingKop = 0L;
            foreach (var quarter in kind.Quarters)
            {
                Assert.Equal(openingKop, quarter.OpeningBalanceKop);
                Assert.Equal(
                    quarter.OpeningBalanceKop + quarter.AccruedKop - quarter.PaidKop,
                    quarter.BalanceKop);
                openingKop = quarter.BalanceKop;
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_kinds_accruals_add_up_to_the_cumulative_figure_the_accrual_reports(int scenario)
    {
        var accrual = Accruals.ForYear(
            2026, ScenarioTransactions[scenario], Config2026, RegisteredIn2025);
        var balances = Balances.ForYear(accrual, ScenarioPayments[scenario]);

        for (var quarter = 1; quarter <= 4; quarter++)
        {
            Assert.Equal(
                accrual.Quarters[quarter - 1].CumulativeSingleTaxKop,
                balances.SingleTax.Quarters.Take(quarter).Sum(row => row.AccruedKop));
            Assert.Equal(
                accrual.Quarters[quarter - 1].CumulativeMilitaryLevyKop,
                balances.MilitaryLevy.Quarters.Take(quarter).Sum(row => row.AccruedKop));
            Assert.Equal(
                accrual.Quarters.Take(quarter).Sum(row => row.EsvKop),
                balances.Esv.Quarters.Take(quarter).Sum(row => row.AccruedKop));
        }
    }

    [Fact]
    public void A_refund_that_turns_a_quarters_accrual_negative_leaves_the_cumulative_debt_intact()
    {
        var actual = Balance(RefundAcrossQuarters);

        Assert.Equal(
            [50_000L, -100_000L, 100_000L, 100_000L],
            actual.SingleTax.Quarters.Select(quarter => quarter.BalanceKop));
        Assert.Equal(
            [10_000L, -20_000L, 20_000L, 20_000L],
            actual.MilitaryLevy.Quarters.Select(quarter => quarter.BalanceKop));
    }

    [Fact]
    public void The_accruals_warnings_reach_the_balances()
    {
        var withoutRegistration = Balances.ForYear(
            Accruals.ForYear(2026, OneReceipt, Config2026, Settings(null)), []);
        var withARefund = Balance(RefundAcrossQuarters);

        Assert.Equal(
            [new EngineWarning.FopRegistrationDateNotSet()], withoutRegistration.Warnings);
        Assert.Equal([new EngineWarning.NegativeCumulativeTax(2)], withARefund.Warnings);
    }

    [Fact]
    public void A_negative_payment_is_rejected()
    {
        var actual = Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetPaymentInput(
            PaymentKind.Esv, -1, 2026, new PaymentPeriod.Quarterly(1)));

        Assert.Equal("amountKop", actual.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void A_period_quarter_outside_one_to_four_is_rejected(int quarter)
    {
        var actual = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaymentPeriod.Quarterly(quarter));

        Assert.Equal("quarter", actual.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void A_period_month_outside_one_to_twelve_is_rejected_as_a_month(int month)
    {
        var actual = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaymentPeriod.Monthly(month));

        Assert.Equal("month", actual.ParamName);
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

    /// <summary>Q1 accrues 50 000 of single tax and 10 000 of levy, every quarter 570 702 of ESV.</summary>
    private static readonly TransactionInput[] OneReceipt =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

    /// <summary>
    /// A refund large enough to make Q2's cumulative income negative, then a receipt that lifts it
    /// back. Single tax accrues 50 000, -150 000, 200 000, 0 across the four quarters.
    /// </summary>
    private static readonly TransactionInput[] RefundAcrossQuarters =
    [
        new TransactionInput.Income(Date("2026-02-10"), 1_000_000),
        new TransactionInput.RefundToClient(Date("2026-04-15"), 3_000_000),
        new TransactionInput.Income(Date("2026-08-10"), 4_000_000),
    ];

    private static readonly TransactionInput[][] ScenarioTransactions =
    [
        OneReceipt,
        OneReceipt,
        OneReceipt,
        RefundAcrossQuarters,
        [],
    ];

    private static readonly BudgetPaymentInput[][] ScenarioPayments =
    [
        [],
        [Paid(PaymentKind.Esv, 2 * EsvQuarterKop, 1)],
        [Paid(PaymentKind.SingleTax, 50_000, 1), Paid(PaymentKind.SingleTax, 30_000, 3)],
        [Paid(PaymentKind.MilitaryLevy, 100_000, 2), Paid(PaymentKind.Esv, 5_000_000, 4)],
        [Paid(PaymentKind.Esv, 1, 3)],
    ];

    private static readonly YearBalances[] Scenarios =
        [.. ScenarioTransactions.Select((transactions, index) => Balances.ForYear(
            Accruals.ForYear(2026, transactions, Config2026, RegisteredIn2025),
            ScenarioPayments[index]))];

    private static YearBalances Balance(
        TransactionInput[] transactions,
        params BudgetPaymentInput[] payments) =>
        Balances.ForYear(
            Accruals.ForYear(2026, transactions, Config2026, RegisteredIn2025), payments);

    private static BudgetPaymentInput Paid(PaymentKind kind, long amountKop, int quarter) =>
        new(kind, amountKop, 2026, new PaymentPeriod.Quarterly(quarter));

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
