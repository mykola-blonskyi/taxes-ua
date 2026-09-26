using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class ObligationBuilderTests
{
    private const long EsvQuarterKop = 570_702;

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
        var actual = Find(Build(OneReceipt, Date(today)), kind, quarter);

        Assert.Equal(expected, actual.Status);
    }

    [Fact]
    public void An_obligation_settled_in_full_is_done_however_late_the_day_is()
    {
        var obligations = Build(
            OneReceipt,
            Date("2027-06-01"),
            Paid(PaymentKind.SingleTax, 50_000, 1),
            Paid(PaymentKind.MilitaryLevy, 10_000, 1),
            Paid(PaymentKind.Esv, 4 * EsvQuarterKop, 1));

        Assert.All(obligations, obligation =>
            Assert.Equal(ObligationStatus.Done, obligation.Status));
    }

    [Fact]
    public void With_nothing_accrued_and_nothing_paid_every_obligation_is_done()
    {
        var accrual = Accruals.ForYear(
            2026, [], Config2026, RegisteredIn2025 with { EsvExempt = true });

        var obligations = ObligationBuilder.ForYear(
            Balances.ForYear(accrual, []),
            Config2026,
            RegisteredIn2025 with { EsvExempt = true },
            Date("2027-06-01"));

        Assert.All(obligations, obligation =>
            Assert.Equal((0L, 0L, ObligationStatus.Done),
                (obligation.AccruedKop, obligation.BalanceKop, obligation.Status)));
    }

    [Fact]
    public void The_year_yields_one_obligation_per_kind_per_quarter_in_quarter_then_kind_order()
    {
        var actual = Build(OneReceipt, Date("2026-05-25"));

        Assert.Equal(
            [
                (1, PaymentKind.SingleTax), (1, PaymentKind.MilitaryLevy), (1, PaymentKind.Esv),
                (2, PaymentKind.SingleTax), (2, PaymentKind.MilitaryLevy), (2, PaymentKind.Esv),
                (3, PaymentKind.SingleTax), (3, PaymentKind.MilitaryLevy), (3, PaymentKind.Esv),
                (4, PaymentKind.SingleTax), (4, PaymentKind.MilitaryLevy), (4, PaymentKind.Esv),
            ],
            actual.Select(obligation => (obligation.Quarter, obligation.Kind)));
        Assert.All(actual, obligation => Assert.Equal(2026, obligation.Year));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_single_tax_and_the_levy_take_the_payment_deadline_and_esv_takes_its_own(
        int quarter)
    {
        var deadlines = DeadlineCalendar.ForQuarter(2026, quarter, Config2026, RegisteredIn2025);
        var obligations = Build(OneReceipt, Date("2026-05-25"));

        Assert.Equal(
            [
                (deadlines.TaxPayment.Statutory, deadlines.TaxPayment.Due),
                (deadlines.TaxPayment.Statutory, deadlines.TaxPayment.Due),
                (deadlines.Esv.Statutory, deadlines.Esv.Due),
            ],
            obligations
                .Where(obligation => obligation.Quarter == quarter)
                .Select(obligation => (obligation.StatutoryDate, obligation.DueDate)));
    }

    [Fact]
    public void A_payment_deadline_that_falls_before_its_declaration_deadline_still_decides_lateness()
    {
        var settings = RegisteredIn2025 with { ShiftTaxPaymentFromWeekend = false };
        var config = Config2026 with { Holidays = MayHolidays };
        var deadlines = DeadlineCalendar.ForQuarter(2026, 1, config, settings);

        var actual = Find(
            ObligationBuilder.ForYear(
                Balances.ForYear(Accruals.ForYear(2026, OneReceipt, config, settings), []),
                config,
                settings,
                Date("2026-05-25")),
            PaymentKind.SingleTax,
            quarter: 1);

        Assert.True(deadlines.TaxPayment.Due < deadlines.Declaration.Due);
        Assert.Equal(
            (Date("2026-05-20"), ObligationStatus.Overdue), (actual.DueDate, actual.Status));
    }

    [Fact]
    public void A_quarter_whose_accrual_went_negative_is_done_and_leaves_the_later_debt_whole()
    {
        var obligations = Build(RefundAcrossQuarters, Date("2027-06-01"));

        Assert.Equal(
            [
                (50_000L, ObligationStatus.Overdue),
                (-100_000L, ObligationStatus.Done),
                (100_000L, ObligationStatus.Overdue),
                (100_000L, ObligationStatus.Overdue),
            ],
            obligations
                .Where(obligation => obligation.Kind == PaymentKind.SingleTax)
                .Select(obligation => (obligation.BalanceKop, obligation.Status)));
    }

    [Fact]
    public void Without_a_registration_date_there_are_no_obligations_at_all()
    {
        var settings = Settings(null);
        var accrual = Accruals.ForYear(2026, OneReceipt, Config2026, settings);

        var actual = ObligationBuilder.ForYear(
            Balances.ForYear(accrual, []), Config2026, settings, Date("2027-06-01"));

        Assert.Empty(actual);
    }

    [Fact]
    public void A_quarter_that_ended_before_the_registration_date_is_still_reported_and_is_done()
    {
        var settings = Settings(Date("2026-08-15"));
        var accrual = Accruals.ForYear(2026, OneReceipt, Config2026, settings);

        var actual = ObligationBuilder.ForYear(
            Balances.ForYear(accrual, []), Config2026, settings, Date("2027-06-01"));

        Assert.Equal(12, actual.Count);
        Assert.All(
            actual.Where(obligation => obligation.Quarter == 1),
            obligation => Assert.Equal(
                (0L, ObligationStatus.Done), (obligation.AccruedKop, obligation.Status)));
    }

    [Fact]
    public void An_obligation_carries_the_balance_row_its_kind_and_quarter_produced()
    {
        var balances = Balances.ForYear(
            Accruals.ForYear(2026, OneReceipt, Config2026, RegisteredIn2025),
            [Paid(PaymentKind.Esv, 2 * EsvQuarterKop, 1)]);

        var actual = Find(
            ObligationBuilder.ForYear(balances, Config2026, RegisteredIn2025, Date("2026-05-25")),
            PaymentKind.Esv,
            quarter: 2);
        var expected = balances.Esv.Quarters[1];

        Assert.Equal(
            (expected.AccruedKop, expected.PaidKop, expected.OpeningBalanceKop,
                expected.BalanceKop),
            (actual.AccruedKop, actual.PaidKop, actual.OpeningBalanceKop, actual.BalanceKop));
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

    private static readonly TransactionInput[] OneReceipt =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

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

    private static IReadOnlyList<Obligation> Build(
        TransactionInput[] transactions,
        DateOnly today,
        params BudgetPaymentInput[] payments) =>
        ObligationBuilder.ForYear(
            Balances.ForYear(
                Accruals.ForYear(2026, transactions, Config2026, RegisteredIn2025), payments),
            Config2026,
            RegisteredIn2025,
            today);

    private static Obligation Find(
        IReadOnlyList<Obligation> obligations, PaymentKind kind, int quarter) =>
        obligations.Single(
            obligation => obligation.Kind == kind && obligation.Quarter == quarter);

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
