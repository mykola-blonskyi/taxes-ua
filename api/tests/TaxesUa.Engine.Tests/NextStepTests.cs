using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class NextStepTests
{
    private const long EsvQuarterKop = 570_702;

    [Fact]
    public void Of_several_obligations_the_nearest_is_now_and_the_rest_are_later()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-04-01")));

        Assert.Equal([Debt(PaymentKind.Esv, 2026, 1, 2026, 1, EsvQuarterKop, "2026-04-20", ObligationStatus.Upcoming)], step.Now);
        Assert.Equal(
            [
                Debt(PaymentKind.SingleTax, 2026, 1, 2026, 1, 50_000, "2026-05-20", ObligationStatus.Upcoming),
                Debt(PaymentKind.MilitaryLevy, 2026, 1, 2026, 1, 10_000, "2026-05-20", ObligationStatus.Upcoming),
            ],
            step.Later);
    }

    [Fact]
    public void The_single_tax_and_the_levy_share_a_deadline_and_stay_two_amounts()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-04-25"), Paid(PaymentKind.Esv, EsvQuarterKop, 2026, 1)));

        Assert.Equal(
            [(PaymentKind.SingleTax, 50_000L), (PaymentKind.MilitaryLevy, 10_000L)],
            step.Now.Select(debt => (debt.Kind, debt.AmountKop)));
        Assert.Equal([Debt(PaymentKind.Esv, 2026, 2, 2026, 2, EsvQuarterKop, "2026-07-20", ObligationStatus.Upcoming)], step.Later);
    }

    [Theory]
    [InlineData("2026-04-19", ObligationStatus.Upcoming)]
    [InlineData("2026-04-20", ObligationStatus.Due)]
    [InlineData("2026-04-21", ObligationStatus.Overdue)]
    public void The_step_carries_the_status_the_engine_gives_its_obligation(string today, ObligationStatus expected)
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date(today)));

        Assert.Equal((PaymentKind.Esv, Date("2026-04-20"), expected), (step.Now[0].Kind, step.Now[0].DueDate, step.Now[0].Status));
    }

    [Fact]
    public void Quarters_that_have_fallen_due_are_one_debt_overdue_since_the_oldest()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-07-25")));

        Assert.Equal(
            Debt(PaymentKind.Esv, 2026, 1, 2026, 2, 2 * EsvQuarterKop, "2026-04-20", ObligationStatus.Overdue),
            Assert.Single(step.Now.Concat(step.Later), debt => debt.Kind == PaymentKind.Esv));
    }

    [Fact]
    public void Every_overdue_kind_is_now_even_when_their_deadlines_differ()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-05-25")));

        Assert.Equal(
            [(PaymentKind.Esv, Date("2026-04-20")), (PaymentKind.SingleTax, Date("2026-05-20")), (PaymentKind.MilitaryLevy, Date("2026-05-20"))],
            step.Now.Select(debt => (debt.Kind, debt.DueDate)));
        Assert.All(step.Now, debt => Assert.Equal(ObligationStatus.Overdue, debt.Status));
        Assert.Empty(step.Later);
    }

    [Fact]
    public void A_debt_due_today_joins_an_older_overdue_one()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-05-20"), Paid(PaymentKind.SingleTax, 50_000, 2026, 1)));

        Assert.Equal(
            [(PaymentKind.Esv, ObligationStatus.Overdue), (PaymentKind.MilitaryLevy, ObligationStatus.Due)],
            step.Now.Select(debt => (debt.Kind, debt.Status)));
    }

    [Fact]
    public void A_quarter_due_today_on_top_of_an_older_one_is_overdue_not_due()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-07-20")));

        Assert.Equal(
            Debt(PaymentKind.Esv, 2026, 1, 2026, 2, 2 * EsvQuarterKop, "2026-04-20", ObligationStatus.Overdue),
            step.Now[0]);
    }

    [Fact]
    public void A_payment_named_for_a_later_quarter_settles_the_oldest_first()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-07-25"), Paid(PaymentKind.Esv, 2 * EsvQuarterKop, 2026, 2)));

        Assert.Equal(
            Debt(PaymentKind.Esv, 2026, 3, 2026, 3, EsvQuarterKop, "2026-10-19", ObligationStatus.Upcoming),
            Assert.Single(step.Now.Concat(step.Later), debt => debt.Kind == PaymentKind.Esv));
    }

    [Fact]
    public void A_prepaid_quarter_moves_the_kind_to_its_next_open_quarter()
    {
        var step = Assert.IsType<NextStep.Pay>(Find(Date("2026-04-01"), Paid(PaymentKind.Esv, EsvQuarterKop, 2026, 1)));

        Assert.Contains(Debt(PaymentKind.Esv, 2026, 2, 2026, 2, EsvQuarterKop, "2026-07-20", ObligationStatus.Upcoming), step.Later);
    }

    [Fact]
    public void Every_kind_settled_is_all_done()
    {
        var step = Find(
            Date("2027-03-01"),
            Paid(PaymentKind.SingleTax, 50_000, 2026, 1),
            Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1),
            Paid(PaymentKind.Esv, 4 * EsvQuarterKop, 2026, 1));

        Assert.IsType<NextStep.AllDone>(step);
    }

    [Fact]
    public void Nothing_accrued_is_all_done()
    {
        var settings = RegisteredIn2025 with { EsvExempt = true };
        var ledger = Ledger(settings, Date("2026-06-01"), [Year(2026, [], settings)], []);

        Assert.IsType<NextStep.AllDone>(NextStep.Find(ledger, settings.FopRegistrationDate, Date("2026-06-01"), advances: null));
    }

    [Fact]
    public void A_day_before_registration_is_a_hint_with_the_registration_date()
    {
        var registered = Date("2026-10-01");
        var settings = Settings(registered);
        var ledger = Ledger(settings, Date("2026-09-30"), [Year(2026, [], settings)], []);

        Assert.Equal(new NextStep.BeforeRegistration(registered), NextStep.Find(ledger, registered, Date("2026-09-30"), advances: null));
    }

    [Fact]
    public void The_registration_day_itself_is_no_longer_before_registration()
    {
        var registered = Date("2026-10-01");
        var settings = Settings(registered);
        var ledger = Ledger(settings, registered, [Year(2026, [], settings)], []);

        var step = Assert.IsType<NextStep.Pay>(NextStep.Find(ledger, registered, registered, advances: null));

        Assert.Equal([Debt(PaymentKind.Esv, 2026, 4, 2026, 4, EsvQuarterKop, "2027-01-19", ObligationStatus.Upcoming)], step.Now);
    }

    [Fact]
    public void Without_a_registration_date_there_is_no_step()
    {
        var settings = Settings(null);
        var ledger = Ledger(settings, Date("2026-06-01"), [Year(2026, OneReceipt, settings)], []);

        Assert.IsType<NextStep.RegistrationDateNotSet>(NextStep.Find(ledger, null, Date("2026-06-01"), advances: null));
    }

    [Fact]
    public void An_arrear_spanning_two_years_is_one_debt_per_kind()
    {
        var today = Date("2026-04-25");
        var step = Assert.IsType<NextStep.Pay>(TwoYears(today, Paid2025ThroughQ3()));

        Assert.Equal(
            [Debt(PaymentKind.Esv, 2025, 4, 2026, 1, 2 * EsvQuarterKop, "2026-01-19", ObligationStatus.Overdue)],
            step.Now);
        Assert.Equal([PaymentKind.SingleTax, PaymentKind.MilitaryLevy], step.Later.Select(debt => debt.Kind));
    }

    [Fact]
    public void Paying_each_shown_amount_against_its_oldest_quarter_clears_exactly_that_debt()
    {
        var today = Date("2026-04-25");
        var before = Assert.IsType<NextStep.Pay>(TwoYears(today, Paid2025ThroughQ3()));
        var marked = before.Now
            .Select(debt => new BudgetPaymentInput(
                debt.Kind, debt.AmountKop, debt.FromYear, new PaymentPeriod.Quarterly(debt.FromQuarter)));

        var after = Assert.IsType<NextStep.Pay>(TwoYears(today, [.. Paid2025ThroughQ3(), .. marked]));

        Assert.Equal(before.Later, after.Now);
        Assert.Equal(
            [Debt(PaymentKind.Esv, 2026, 2, 2026, 2, EsvQuarterKop, "2026-07-20", ObligationStatus.Upcoming)],
            after.Later);
    }

    [Fact]
    public void In_january_the_step_is_the_previous_years_fourth_quarter()
    {
        var today = Date("2027-01-10");
        var ledger = Ledger(
            RegisteredIn2025,
            today,
            [Year(2026, OneReceipt, RegisteredIn2025), Year(2027, [], RegisteredIn2025)],
            [
                Paid(PaymentKind.SingleTax, 50_000, 2026, 1),
                Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1),
                Paid(PaymentKind.Esv, 3 * EsvQuarterKop, 2026, 1),
            ]);

        var step = Assert.IsType<NextStep.Pay>(NextStep.Find(ledger, RegisteredIn2025.FopRegistrationDate, today, advances: null));

        Assert.Equal([Debt(PaymentKind.Esv, 2026, 4, 2026, 4, EsvQuarterKop, "2027-01-19", ObligationStatus.Upcoming)], step.Now);
        Assert.Empty(step.Later);
    }

    [Fact]
    public void The_burden_counts_only_the_quarters_through_the_one_asked_for()
    {
        var accrual = Accruals.ForYear(2026, OneReceipt, Config2026, RegisteredIn2025);

        var burden = accrual.BurdenThrough(1);

        Assert.Equal((1_000_000L, 50_000L + 10_000L + EsvQuarterKop), (burden.IncomeKop, burden.TaxKop));
        Assert.Equal(6_307L, burden.RateBp);
    }

    [Fact]
    public void The_burden_rounds_half_up_to_a_basis_point()
    {
        Assert.Equal(1L, new TaxBurden(IncomeKop: 20_000, TaxKop: 1).RateBp);
        Assert.Equal(0L, new TaxBurden(IncomeKop: 20_001, TaxKop: 1).RateBp);
    }

    [Fact]
    public void Without_income_the_burden_has_no_rate()
    {
        var accrual = Accruals.ForYear(2026, [], Config2026, RegisteredIn2025);

        Assert.Null(accrual.BurdenThrough(3).RateBp);
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
        IncomeLimitKop: 1_009_104_900,
        ExcessRateBp: 1_500,
        LimitWarnThresholdsPct: [85, 100]);

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static readonly TransactionInput[] OneReceipt =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

    private static NextStep Find(DateOnly today, params BudgetPaymentInput[] payments) =>
        NextStep.Find(
            Ledger(RegisteredIn2025, today, [Year(2026, OneReceipt, RegisteredIn2025)], payments),
            RegisteredIn2025.FopRegistrationDate,
            today,
            advances: null);

    private static NextStep TwoYears(DateOnly today, BudgetPaymentInput[] payments) =>
        NextStep.Find(
            Ledger(
                RegisteredIn2025,
                today,
                [Year(2025, [], RegisteredIn2025), Year(2026, OneReceipt, RegisteredIn2025)],
                payments),
            RegisteredIn2025.FopRegistrationDate,
            today,
            advances: null);

    private static BudgetPaymentInput[] Paid2025ThroughQ3() => [Paid(PaymentKind.Esv, 3 * EsvQuarterKop, 2025, 1)];

    private static LedgerYear Year(int year, TransactionInput[] transactions, FopSettingsInput settings) =>
        new(Accruals.ForYear(year, transactions, Config2026, settings), Config2026);

    private static PaymentLedger Ledger(
        FopSettingsInput settings, DateOnly today, LedgerYear[] years, BudgetPaymentInput[] payments) =>
        Balances.ForYears(years, settings, payments, today);

    private static KindDebt Debt(
        PaymentKind kind, int fromYear, int fromQuarter, int toYear, int toQuarter, long amountKop, string dueDate, ObligationStatus status) =>
        new(kind, fromYear, fromQuarter, toYear, toQuarter, amountKop, Date(dueDate), status, AdvanceMonth: null);

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
