using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class BalancesTests
{
    private const long EsvQuarterKop = 570_702;
    private const long EsvYearKop = 4 * EsvQuarterKop;

    [Fact]
    public void A_payment_named_for_q2_settles_the_unpaid_q1_levy_first_and_then_q2()
    {
        var ledger = Ledger(
            [Year(2026, TwoQuartersOfLevy)],
            Date("2026-06-01"),
            Paid(PaymentKind.MilitaryLevy, 200_000, 2026, quarter: 2)).MilitaryLevy;

        Assert.Equal(
            [
                (1, 100_000L, 100_000L, 0L, ObligationStatus.Done),
                (2, 100_000L, 100_000L, 0L, ObligationStatus.Done),
            ],
            Rows(ledger, 2026).Take(2));
        Assert.Equal((0L, 0L), (ledger.CreditKop, ledger.BalanceKop));
    }

    [Fact]
    public void A_partial_payment_settles_the_oldest_obligation_and_leaves_the_rest_on_the_next()
    {
        var ledger = Ledger(
            [Year(2026, TwoQuartersOfLevy)],
            Date("2026-06-01"),
            Paid(PaymentKind.MilitaryLevy, 150_000, 2026, quarter: 2)).MilitaryLevy;

        Assert.Equal(
            [
                (1, 100_000L, 100_000L, 0L, ObligationStatus.Done),
                (2, 100_000L, 50_000L, 50_000L, ObligationStatus.Upcoming),
            ],
            Rows(ledger, 2026).Take(2));
        Assert.Equal((0L, 50_000L), (ledger.CreditKop, ledger.BalanceKop));
    }

    [Fact]
    public void An_overpayment_in_december_covers_the_next_years_first_quarter()
    {
        var ledger = Ledger(
            [Year(2026, OneReceipt), Year(2027, [])],
            Date("2027-05-01"),
            Paid(PaymentKind.Esv, 5 * EsvQuarterKop, 2026, quarter: 4)).Esv;

        Assert.All(
            Rows(ledger, 2026), row => Assert.Equal(ObligationStatus.Done, row.Status));
        Assert.Equal(
            [
                (1, EsvQuarterKop, EsvQuarterKop, 0L, ObligationStatus.Done),
                (2, EsvQuarterKop, 0L, EsvQuarterKop, ObligationStatus.Upcoming),
            ],
            Rows(ledger, 2027).Take(2));
        Assert.Equal(
            new KindYearBalance(0, EsvYearKop, EsvQuarterKop, 3 * EsvQuarterKop, 0),
            ledger.ForYear(2027));
        Assert.Equal(
            new KindYearBalance(0, EsvYearKop, EsvYearKop, 0, 0),
            ledger.ForYear(2026));
    }

    [Fact]
    public void A_year_settled_by_a_payment_named_for_the_next_year_reads_as_settled_in_its_own_year()
    {
        var ledger = Ledger(
            [Year(2026, OneReceipt), Year(2027, [])],
            Date("2027-06-01"),
            Paid(PaymentKind.SingleTax, 50_000, 2027, quarter: 1)).SingleTax;

        Assert.Equal(new KindYearBalance(0, 50_000, 50_000, 0, 0), ledger.ForYear(2026));
        Assert.Equal(new KindYearBalance(0, 0, 0, 0, 0), ledger.ForYear(2027));
    }

    [Fact]
    public void An_unpaid_fourth_quarter_is_still_overdue_in_the_next_year_and_opens_it_owed()
    {
        var ledger = Ledger(
            [Year(2026, FourthQuarterReceipt), Year(2027, [])],
            Date("2027-03-01")).SingleTax;

        var fourth = Assert.Single(Of(ledger, 2026), obligation => obligation.Quarter == 4);
        Assert.Equal(
            (50_000L, Date("2027-02-19"), ObligationStatus.Overdue),
            (fourth.RemainingKop, fourth.DueDate, fourth.Status));
        Assert.Equal(new KindYearBalance(50_000, 0, 0, 50_000, 0), ledger.ForYear(2027));
    }

    [Fact]
    public void A_payment_named_for_the_next_years_first_quarter_settles_the_older_fourth_quarter_first()
    {
        var ledger = Ledger(
            [Year(2026, FourthQuarterReceipt), Year(2027, FirstQuarterReceipt2027)],
            Date("2027-03-01"),
            Paid(PaymentKind.SingleTax, 50_000, 2027, quarter: 1)).SingleTax;

        var fourth = Assert.Single(Of(ledger, 2026), obligation => obligation.Quarter == 4);
        var first = Assert.Single(Of(ledger, 2027), obligation => obligation.Quarter == 1);
        Assert.Equal((50_000L, 0L, ObligationStatus.Done), (fourth.PaidKop, fourth.RemainingKop, fourth.Status));
        Assert.Equal((0L, 50_000L, ObligationStatus.Upcoming), (first.PaidKop, first.RemainingKop, first.Status));
        Assert.Equal(new KindYearBalance(0, 50_000, 0, 50_000, 0), ledger.ForYear(2027));
        Assert.Equal(new KindYearBalance(0, 50_000, 50_000, 0, 0), ledger.ForYear(2026));
    }

    [Fact]
    public void An_overpayment_of_the_single_tax_never_reduces_a_levy_or_esv_debt()
    {
        var actual = Ledger(
            [Year(2026, OneReceipt)],
            Date("2027-06-01"),
            Paid(PaymentKind.SingleTax, 50_000 + 10_000 + EsvYearKop, 2026, quarter: 1));

        Assert.Equal(
            (10_000L + EsvYearKop, 0L),
            (actual.SingleTax.CreditKop, Of(actual.SingleTax, 2026).Sum(row => row.RemainingKop)));
        Assert.All(
            Of(actual.SingleTax, 2026), row => Assert.Equal(ObligationStatus.Done, row.Status));
        Assert.Equal(
            (0L, 10_000L, ObligationStatus.Overdue),
            (actual.MilitaryLevy.CreditKop, actual.MilitaryLevy.BalanceKop,
                Of(actual.MilitaryLevy, 2026)[0].Status));
        Assert.Equal(
            (0L, EsvYearKop),
            (actual.Esv.CreditKop, actual.Esv.BalanceKop));
        Assert.All(
            Of(actual.Esv, 2026), row => Assert.Equal(ObligationStatus.Overdue, row.Status));
    }

    [Theory]
    [InlineData(PaymentKind.SingleTax)]
    [InlineData(PaymentKind.MilitaryLevy)]
    [InlineData(PaymentKind.Esv)]
    public void A_payment_of_one_kind_is_pooled_into_that_kind_and_into_no_other(PaymentKind paidKind)
    {
        var payment = Paid(paidKind, 1_000_000, 2026, quarter: 1);

        var actual = Ledger([Year(2026, OneReceipt)], Date("2026-05-25"), payment);

        foreach (var ledger in new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv })
        {
            Assert.Equal(
                ledger.Kind == paidKind ? new[] { payment } : [],
                ledger.Payments);
            Assert.All(ledger.Obligations, row => Assert.Equal(ledger.Kind, row.Kind));
        }
    }

    [Fact]
    public void Without_a_registration_date_nothing_accrues_and_every_payment_is_credit()
    {
        var settings = Settings(null);
        var payment = Paid(PaymentKind.Esv, 5_000, 2026, quarter: 3);

        var actual = Balances.ForYears(
            [new LedgerYear(Accruals.ForYear(2026, OneReceipt, Config, settings), Config)],
            settings,
            [payment],
            Date("2027-06-01"));

        Assert.All(
            new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv },
            ledger => Assert.Empty(ledger.Obligations));
        Assert.Equal([payment], actual.Esv.Payments);
        Assert.Equal((5_000L, -5_000L), (actual.Esv.CreditKop, actual.Esv.BalanceKop));
        Assert.Equal(new KindYearBalance(0, 0, 0, 0, 5_000), actual.Esv.ForYear(2026));
    }

    [Fact]
    public void A_payment_named_after_the_last_year_in_range_is_outside_the_ledger()
    {
        var later = Paid(PaymentKind.SingleTax, 50_000, 2027, quarter: 1);

        var actual = Ledger([Year(2026, OneReceipt)], Date("2027-06-01"), later).SingleTax;

        Assert.Empty(actual.Payments);
        Assert.Equal((50_000L, 0L), (actual.BalanceKop, actual.CreditKop));
        Assert.Equal(ObligationStatus.Overdue, Of(actual, 2026)[0].Status);
    }

    [Fact]
    public void A_payment_named_for_a_year_before_registration_is_credit_against_the_oldest_debt()
    {
        var earlier = Paid(PaymentKind.SingleTax, 80_000, 2024, quarter: 4);

        var actual = Ledger([Year(2026, OneReceipt)], Date("2027-06-01"), earlier).SingleTax;

        Assert.Equal(
            (1, 50_000L, 50_000L, 0L, ObligationStatus.Done), Rows(actual, 2026)[0]);
        Assert.Equal((30_000L, -30_000L), (actual.CreditKop, actual.BalanceKop));
        Assert.Equal(new KindYearBalance(0, 50_000, 50_000, 0, 30_000), actual.ForYear(2026));
    }

    [Fact]
    public void A_refund_quarters_negative_accrual_is_done_and_its_credit_settles_the_oldest_debt()
    {
        var actual = Ledger([Year(2026, RefundAcrossQuarters)], Date("2027-06-01")).SingleTax;

        Assert.Equal(
            [
                (1, 50_000L, 50_000L, 0L, ObligationStatus.Done),
                (2, -150_000L, 0L, 0L, ObligationStatus.Done),
                (3, 200_000L, 100_000L, 100_000L, ObligationStatus.Overdue),
                (4, 0L, 0L, 0L, ObligationStatus.Done),
            ],
            Rows(actual, 2026));
        Assert.Equal((0L, 100_000L), (actual.CreditKop, actual.BalanceKop));
    }

    [Theory]
    [InlineData(PaymentKind.Esv, 1, "2026-04-19", ObligationStatus.Upcoming)]
    [InlineData(PaymentKind.Esv, 1, "2026-04-20", ObligationStatus.Due)]
    [InlineData(PaymentKind.Esv, 1, "2026-04-21", ObligationStatus.Overdue)]
    [InlineData(PaymentKind.SingleTax, 1, "2026-05-19", ObligationStatus.Upcoming)]
    [InlineData(PaymentKind.SingleTax, 1, "2026-05-20", ObligationStatus.Due)]
    [InlineData(PaymentKind.SingleTax, 1, "2026-05-21", ObligationStatus.Overdue)]
    [InlineData(PaymentKind.MilitaryLevy, 1, "2026-05-20", ObligationStatus.Due)]
    [InlineData(PaymentKind.Esv, 4, "2027-01-18", ObligationStatus.Upcoming)]
    [InlineData(PaymentKind.Esv, 4, "2027-01-19", ObligationStatus.Due)]
    [InlineData(PaymentKind.Esv, 4, "2027-01-20", ObligationStatus.Overdue)]
    public void An_unpaid_obligation_is_overdue_only_after_its_due_date_has_passed(
        PaymentKind kind,
        int quarter,
        string today,
        ObligationStatus expected)
    {
        var ledger = Ledger([Year(2026, OneReceipt)], Date(today)).Of(kind);

        Assert.Equal(expected, Of(ledger, 2026)[quarter - 1].Status);
    }

    [Fact]
    public void An_obligation_settled_in_full_is_done_however_late_the_day_is()
    {
        var actual = Ledger(
            [Year(2026, OneReceipt)],
            Date("2030-06-01"),
            Paid(PaymentKind.SingleTax, 50_000, 2026, quarter: 1),
            Paid(PaymentKind.MilitaryLevy, 10_000, 2026, quarter: 1),
            Paid(PaymentKind.Esv, EsvYearKop, 2026, quarter: 1));

        Assert.All(
            new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv }.SelectMany(l => l.Obligations),
            obligation => Assert.Equal(ObligationStatus.Done, obligation.Status));
    }

    [Fact]
    public void With_nothing_accrued_and_nothing_paid_every_obligation_is_done()
    {
        var settings = RegisteredIn2025 with { EsvExempt = true };

        var actual = Balances.ForYears(
            [new LedgerYear(Accruals.ForYear(2026, [], Config, settings), Config)],
            settings,
            [],
            Date("2027-06-01"));

        Assert.All(
            new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv }.SelectMany(l => l.Obligations),
            obligation => Assert.Equal(
                (0L, 0L, ObligationStatus.Done),
                (obligation.AccruedKop, obligation.RemainingKop, obligation.Status)));
    }

    [Fact]
    public void Each_kind_has_one_obligation_per_quarter_of_every_year_in_due_date_order()
    {
        var actual = Ledger([Year(2027, []), Year(2026, OneReceipt)], Date("2026-05-25"));

        foreach (var ledger in new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv })
        {
            Assert.Equal(
                [(2026, 1), (2026, 2), (2026, 3), (2026, 4), (2027, 1), (2027, 2), (2027, 3), (2027, 4)],
                ledger.Obligations.Select(obligation => (obligation.Year, obligation.Quarter)));
            Assert.Equal(
                ledger.Obligations.OrderBy(obligation => obligation.DueDate),
                ledger.Obligations);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_single_tax_and_the_levy_take_the_payment_deadline_and_esv_takes_its_own(
        int quarter)
    {
        var deadlines = DeadlineCalendar.ForQuarter(2026, quarter, Config, RegisteredIn2025);
        var actual = Ledger([Year(2026, OneReceipt)], Date("2026-05-25"));

        Assert.Equal(
            [
                (deadlines.TaxPayment.Statutory, deadlines.TaxPayment.Due),
                (deadlines.TaxPayment.Statutory, deadlines.TaxPayment.Due),
                (deadlines.Esv.Statutory, deadlines.Esv.Due),
            ],
            new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv }
                .Select(ledger => Of(ledger, 2026)[quarter - 1])
                .Select(obligation => (obligation.StatutoryDate, obligation.DueDate)));
    }

    [Fact]
    public void A_payment_deadline_that_falls_before_its_declaration_deadline_still_decides_lateness()
    {
        var settings = RegisteredIn2025 with { ShiftTaxPaymentFromWeekend = false };
        var config = Config with { Holidays = MayHolidays };
        var deadlines = DeadlineCalendar.ForQuarter(2026, 1, config, settings);

        var actual = Of(
            Balances.ForYears(
                [new LedgerYear(Accruals.ForYear(2026, OneReceipt, config, settings), config)],
                settings,
                [],
                Date("2026-05-25")).SingleTax,
            2026)[0];

        Assert.True(deadlines.TaxPayment.Due < deadlines.Declaration.Due);
        Assert.Equal(
            (Date("2026-05-20"), ObligationStatus.Overdue), (actual.DueDate, actual.Status));
    }

    [Fact]
    public void A_quarter_that_ended_before_the_registration_date_is_still_reported_and_is_done()
    {
        var settings = Settings(Date("2026-08-15"));

        var actual = Balances.ForYears(
            [new LedgerYear(Accruals.ForYear(2026, OneReceipt, Config, settings), Config)],
            settings,
            [],
            Date("2027-06-01"));

        foreach (var ledger in new[] { actual.SingleTax, actual.MilitaryLevy, actual.Esv })
        {
            Assert.Equal(4, ledger.Obligations.Count);
            Assert.Equal(
                (0L, ObligationStatus.Done),
                (Of(ledger, 2026)[0].AccruedKop, Of(ledger, 2026)[0].Status));
        }
    }

    [Fact]
    public void An_obligations_accrual_is_that_quarters_own_accrual_of_its_kind()
    {
        var accrual = Accruals.ForYear(2026, RefundAcrossQuarters, Config, RegisteredIn2025);

        var actual = Balances.ForYears(
            [new LedgerYear(accrual, Config)], RegisteredIn2025, [], Date("2026-05-25"));

        Assert.Equal(
            accrual.Quarters.Select(quarter => quarter.SingleTaxKop),
            Of(actual.SingleTax, 2026).Select(obligation => obligation.AccruedKop));
        Assert.Equal(
            accrual.Quarters.Select(quarter => quarter.MilitaryLevyKop),
            Of(actual.MilitaryLevy, 2026).Select(obligation => obligation.AccruedKop));
        Assert.Equal(
            accrual.Quarters.Select(quarter => quarter.EsvKop),
            Of(actual.Esv, 2026).Select(obligation => obligation.AccruedKop));
    }

    [Fact]
    public void Random_ledgers_allocate_oldest_first_within_a_kind_and_never_across_kinds()
    {
        var random = new Random(47);
        for (var run = 0; run < 600; run++)
        {
            var (years, settings, payments, today) = RandomCase(random);
            var ledger = Balances.ForYears(years, settings, payments, today);
            var horizon = years.Max(year => year.Accrual.Year);
            var inRange = years.Select(year => year.Accrual.Year).ToHashSet();

            foreach (var kind in ledger.All())
            {
                var context = $"run {run}, {kind.Kind}";
                var remainingKop = kind.Obligations.Sum(o => o.RemainingKop);
                var pooledKop = payments
                    .Where(p => p.Kind == kind.Kind && p.PeriodYear <= horizon)
                    .Sum(p => p.AmountKop);

                Assert.True(
                    remainingKop - kind.CreditKop == kind.Obligations.Sum(o => o.AccruedKop) - pooledKop
                        && remainingKop - kind.CreditKop == kind.BalanceKop,
                    context);
                Assert.True(remainingKop == 0 || kind.CreditKop == 0, context);
                Assert.True(kind.CreditKop >= 0, context);

                foreach (var obligation in kind.Obligations)
                {
                    var owedKop = Math.Max(obligation.AccruedKop, 0);
                    Assert.InRange(obligation.PaidKop, 0, owedKop);
                    Assert.Equal(owedKop - obligation.PaidKop, obligation.RemainingKop);
                    Assert.Equal(
                        obligation.RemainingKop == 0, obligation.Status == ObligationStatus.Done);
                }

                for (var i = 0; i < kind.Obligations.Count; i++)
                {
                    if (kind.Obligations[i].RemainingKop > 0)
                    {
                        Assert.True(
                            kind.Obligations
                                .Where(later => later.DueDate > kind.Obligations[i].DueDate)
                                .All(later => later.PaidKop == 0),
                            context);
                    }
                }

                var alone = Balances
                    .ForYears(years, settings, [.. payments.Where(p => p.Kind == kind.Kind)], today)
                    .Of(kind.Kind);
                Assert.Equal(alone.Obligations, kind.Obligations);
                Assert.Equal(alone.Payments, kind.Payments);
                Assert.Equal(alone.CreditKop, kind.CreditKop);

                foreach (var year in inRange)
                {
                    if (inRange.Contains(year + 1))
                    {
                        Assert.Equal(
                            kind.ForYear(year).OwedKop, kind.ForYear(year + 1).EarlierOwedKop);
                    }
                }

                var last = kind.ForYear(horizon);
                Assert.Equal(kind.BalanceKop, last.OwedKop - last.CreditKop);
                Assert.True(last.OwedKop == 0 || last.CreditKop == 0, context);
            }
        }
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

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(12, 4)]
    public void A_month_names_the_quarter_that_contains_it(int month, int quarter)
    {
        Assert.Equal(quarter, new PaymentPeriod.Monthly(month).Quarter);
    }

    [Fact]
    public void A_ledger_without_a_year_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => Balances.ForYears([], RegisteredIn2025, [], Date("2026-05-25")));
    }

    [Fact]
    public void A_ledger_with_the_same_year_twice_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Balances.ForYears(
            [Year(2026, OneReceipt), Year(2026, [])], RegisteredIn2025, [], Date("2026-05-25")));
    }

    private static readonly TaxYearConfigInput Config = new(
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
        LimitWarnThresholdsPct: [85, 100],
        Group3ApplicationDays: 10);

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    /// <summary>Q1 accrues 50 000 of single tax and 10 000 of levy, every quarter 570 702 of ESV.</summary>
    private static readonly TransactionInput[] OneReceipt =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

    /// <summary>Q1 and Q2 each accrue 100 000 of levy.</summary>
    private static readonly TransactionInput[] TwoQuartersOfLevy =
    [
        new TransactionInput.Income(Date("2026-02-10"), 10_000_000),
        new TransactionInput.Income(Date("2026-05-10"), 10_000_000),
    ];

    /// <summary>Q4 2026 accrues 50 000 of single tax, due 19 February 2027.</summary>
    private static readonly TransactionInput[] FourthQuarterReceipt =
        [new TransactionInput.Income(Date("2026-11-10"), 1_000_000)];

    private static readonly TransactionInput[] FirstQuarterReceipt2027 =
        [new TransactionInput.Income(Date("2027-01-15"), 1_000_000)];

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

    /// <summary>
    /// Twenty consecutive non-working days, which is what it takes to push Q1's declaration deadline
    /// past its own payment deadline. Unreachable while martial law keeps the holiday list empty.
    /// </summary>
    private static readonly DateOnly[] MayHolidays =
        [.. Enumerable.Range(10, 20).Select(day => new DateOnly(2026, 5, day))];

    private static (LedgerYear[] Years, FopSettingsInput Settings, BudgetPaymentInput[] Payments, DateOnly Today)
        RandomCase(Random random)
    {
        var settings = RegisteredIn2025 with
        {
            FopRegistrationDate = random.Next(10) == 0
                ? null
                : new DateOnly(2024, 6, 1).AddDays(random.Next(365 * 3 + 200)),
            EsvRegistrationMonthPolicy = random.Next(2) == 0
                ? EsvRegistrationMonthPolicy.FullMonth
                : EsvRegistrationMonthPolicy.Prorated,
            EsvExempt = random.Next(5) == 0,
        };

        var transactions = Enumerable.Range(0, random.Next(12))
            .Select(TransactionInput (_) =>
            {
                var date = new DateOnly(2025, 1, 1).AddDays(random.Next(365 * 3));
                var amountKop = random.NextInt64(1, 5_000_000);
                return random.Next(4) == 0
                    ? new TransactionInput.RefundToClient(date, amountKop)
                    : new TransactionInput.Income(date, amountKop);
            })
            .ToArray();

        var yearNumbers = new[] { 2025, 2026, 2027 }.Where(_ => random.Next(3) != 0).ToList();
        if (yearNumbers.Count == 0)
        {
            yearNumbers.Add(2025 + random.Next(3));
        }

        var years = yearNumbers
            .OrderBy(_ => random.Next())
            .Select(year => new LedgerYear(Accruals.ForYear(year, transactions, Config, settings), Config))
            .ToArray();

        var kinds = Enum.GetValues<PaymentKind>();
        var payments = Enumerable.Range(0, random.Next(10))
            .Select(_ => new BudgetPaymentInput(
                kinds[random.Next(kinds.Length)],
                random.NextInt64(0, 3_000_000),
                2024 + random.Next(5),
                random.Next(2) == 0
                    ? new PaymentPeriod.Quarterly(1 + random.Next(4))
                    : new PaymentPeriod.Monthly(1 + random.Next(12))))
            .ToArray();

        return (years, settings, payments, new DateOnly(2025, 1, 1).AddDays(random.Next(365 * 4)));
    }

    private static LedgerYear Year(int year, TransactionInput[] transactions) =>
        new(Accruals.ForYear(year, transactions, Config, RegisteredIn2025), Config);

    private static PaymentLedger Ledger(
        LedgerYear[] years, DateOnly today, params BudgetPaymentInput[] payments) =>
        Balances.ForYears(years, RegisteredIn2025, payments, today);

    private static Obligation[] Of(KindLedger ledger, int year) =>
        [.. ledger.Obligations.Where(obligation => obligation.Year == year).OrderBy(o => o.Quarter)];

    private static (int Quarter, long AccruedKop, long PaidKop, long RemainingKop, ObligationStatus Status)[]
        Rows(KindLedger ledger, int year) =>
        [.. Of(ledger, year).Select(o => (o.Quarter, o.AccruedKop, o.PaidKop, o.RemainingKop, o.Status))];

    private static BudgetPaymentInput Paid(PaymentKind kind, long amountKop, int year, int quarter) =>
        new(kind, amountKop, year, new PaymentPeriod.Quarterly(quarter));

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

internal static class PaymentLedgerTestExtensions
{
    public static KindLedger[] All(this PaymentLedger ledger) =>
        [ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv];

    public static KindLedger Of(this PaymentLedger ledger, PaymentKind kind) => kind switch
    {
        PaymentKind.SingleTax => ledger.SingleTax,
        PaymentKind.MilitaryLevy => ledger.MilitaryLevy,
        PaymentKind.Esv => ledger.Esv,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
