using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class MonthlyAdvancesTests
{
    private const long EsvMonthKop = 190_234;

    [Fact]
    public void A_quarters_months_add_up_to_its_accrual_to_the_kopeck()
    {
        var accrual = Accrual(Income("2026-01-05", 10), Income("2026-02-05", 10), Income("2026-03-05", 10));

        Assert.Equal([1L, 0L, 1L], accrual.Months.Take(3).Select(month => month.SingleTaxKop));
        Assert.Equal(accrual.Quarters[0].SingleTaxKop, accrual.Months.Take(3).Sum(month => month.SingleTaxKop));
        Assert.Equal(accrual.Quarters[0].EsvKop, accrual.Months.Take(3).Sum(month => month.EsvKop));
    }

    [Fact]
    public void An_advance_larger_than_the_quarterly_accrual_is_an_overpayment()
    {
        var accrual = Accrual(Income("2026-07-10", 1_000_000));
        var ledger = Ledger(accrual, Date("2026-08-01"), Paid(PaymentKind.SingleTax, 80_000, month: 7));

        var q3 = ledger.SingleTax.Obligations.Single(obligation => obligation.Quarter == 3);
        Assert.Equal((50_000L, 50_000L, 0L), (q3.AccruedKop, q3.PaidKop, q3.RemainingKop));
        Assert.Equal(30_000, ledger.SingleTax.CreditKop);
        Assert.All(
            MonthlyAdvances.ForYear(accrual, ledger, 15).Where(month => month.Quarter == 3),
            month => Assert.Equal(0, month.SingleTax.RemainingKop));
    }

    [Fact]
    public void A_partial_advance_leaves_a_remainder_on_the_quarter_and_on_its_oldest_months()
    {
        var accrual = Accrual(Income("2026-07-10", 1_000_000), Income("2026-08-10", 1_000_000));
        // Rule 7 settles the first half-year's ESV before July's, whatever the payment names.
        var ledger = Ledger(accrual, Date("2026-09-01"), Paid(PaymentKind.Esv, 6 * EsvMonthKop + 100_000, month: 7));

        var q3 = ledger.Esv.Obligations.Single(obligation => obligation.Quarter == 3);
        Assert.Equal(3 * EsvMonthKop - 100_000, q3.RemainingKop);
        var months = MonthlyAdvances.ForYear(accrual, ledger, 15).Where(month => month.Quarter == 3).ToArray();
        Assert.Equal([EsvMonthKop - 100_000, EsvMonthKop, EsvMonthKop], months.Select(month => month.Esv.RemainingKop));
        Assert.Equal(q3.RemainingKop, months.Sum(month => month.Esv.RemainingKop));
    }

    [Fact]
    public void A_refund_month_is_credit_to_the_other_months_of_its_quarter()
    {
        var accrual = Accrual(Income("2026-07-10", 1_000_000), Refund("2026-08-10", 400_000));
        var ledger = Ledger(accrual, Date("2026-09-01"));

        var months = MonthlyAdvances.ForYear(accrual, ledger, 15).Where(month => month.Quarter == 3).ToArray();
        Assert.Equal([50_000L, -20_000L, 0L], months.Select(month => month.SingleTax.AccruedKop));
        Assert.Equal([30_000L, 0L, 0L], months.Select(month => month.SingleTax.RemainingKop));
    }

    [Fact]
    public void The_advance_is_due_on_the_recommended_day_of_the_month_after_even_across_the_year_end()
    {
        var advances = MonthlyAdvances.ForYear(Accrual(), Ledger(Accrual(), Date("2026-01-01")), 15);

        Assert.Equal(Date("2026-02-15"), advances[0].RecommendedDate);
        Assert.Equal(Date("2027-01-15"), advances[11].RecommendedDate);
    }

    [Fact]
    public void Switching_modes_changes_only_the_recommendation_and_never_the_ledger()
    {
        var today = Date("2026-08-01");
        var accrual = Accrual(Income("2026-07-10", 1_000_000));
        var paidFirstHalf = Paid(PaymentKind.Esv, 6 * EsvMonthKop, month: 1);
        var ledger = Ledger(accrual, today, paidFirstHalf);
        var before = Figures(ledger);

        var quarterlyStep = Assert.IsType<NextStep.Pay>(NextStep.Find(ledger, Registered, today, advances: null));
        Assert.Equal(
            [(PaymentKind.Esv, 3 * EsvMonthKop, Date("2026-10-19"), (int?)null)],
            quarterlyStep.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)));

        var advanceStep = Assert.IsType<NextStep.Pay>(
            NextStep.Find(ledger, Registered, today, MonthlyAdvances.ForYear(accrual, ledger, 15)));
        Assert.Equal(
            [
                (PaymentKind.SingleTax, 50_000L, Date("2026-08-15"), (int?)7),
                (PaymentKind.MilitaryLevy, 10_000L, Date("2026-08-15"), (int?)7),
                (PaymentKind.Esv, EsvMonthKop, Date("2026-08-15"), (int?)7),
            ],
            advanceStep.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)).OrderBy(debt => debt.Kind));
        Assert.Empty(advanceStep.Later);
        Assert.Equal(before, Figures(ledger));
    }

    [Fact]
    public void A_missed_advance_is_carried_into_the_next_one_and_not_counted_twice()
    {
        var today = Date("2026-08-20");
        var accrual = Accrual(Income("2026-07-10", 1_000_000), Income("2026-08-10", 1_000_000));
        var ledger = Ledger(accrual, today, Paid(PaymentKind.Esv, 6 * EsvMonthKop, month: 1));

        var step = Assert.IsType<NextStep.Pay>(
            NextStep.Find(ledger, Registered, today, MonthlyAdvances.ForYear(accrual, ledger, 15)));

        Assert.Equal(
            [
                (PaymentKind.SingleTax, 100_000L, Date("2026-09-15"), (int?)8),
                (PaymentKind.MilitaryLevy, 20_000L, Date("2026-09-15"), (int?)8),
                (PaymentKind.Esv, 2 * EsvMonthKop, Date("2026-09-15"), (int?)8),
            ],
            step.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)).OrderBy(debt => debt.Kind));
    }

    [Fact]
    public void Once_the_quarters_advances_are_behind_it_the_quarterly_deadline_is_the_step()
    {
        var today = Date("2026-10-16");
        var accrual = Accrual(Income("2026-07-10", 1_000_000));
        var ledger = Ledger(accrual, today, Paid(PaymentKind.Esv, 6 * EsvMonthKop, month: 1));

        var step = Assert.IsType<NextStep.Pay>(
            NextStep.Find(ledger, Registered, today, MonthlyAdvances.ForYear(accrual, ledger, 15)));

        Assert.Equal(
            [(PaymentKind.Esv, 3 * EsvMonthKop, Date("2026-10-19"), (int?)null)],
            step.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)));
    }

    [Fact]
    public void An_overdue_quarter_stays_the_step_whatever_the_mode()
    {
        var today = Date("2026-05-01");
        var accrual = Accrual(Income("2026-02-10", 1_000_000));
        var ledger = Ledger(accrual, today);

        var step = Assert.IsType<NextStep.Pay>(
            NextStep.Find(ledger, Registered, today, MonthlyAdvances.ForYear(accrual, ledger, 15)));

        var esv = Assert.Single(step.Now);
        Assert.Equal((PaymentKind.Esv, ObligationStatus.Overdue, (int?)null), (esv.Kind, esv.Status, esv.AdvanceMonth));
    }

    private static readonly DateOnly Registered = Date("2026-01-01");

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

    private static readonly FopSettingsInput Settings = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: Registered,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static YearAccrual Accrual(params TransactionInput[] transactions) =>
        Accruals.ForYear(2026, transactions, Config2026, Settings);

    private static PaymentLedger Ledger(YearAccrual accrual, DateOnly today, params BudgetPaymentInput[] payments) =>
        Balances.ForYears([new LedgerYear(accrual, Config2026)], Settings, payments, today);

    private static object[] Figures(PaymentLedger ledger) =>
    [
        .. new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }.SelectMany(kind => kind.Obligations),
        ledger.SingleTax.CreditKop,
        ledger.MilitaryLevy.CreditKop,
        ledger.Esv.CreditKop,
    ];

    private static TransactionInput Income(string valueDate, long amountKop) =>
        new TransactionInput.Income(Date(valueDate), amountKop);

    private static TransactionInput Refund(string valueDate, long amountKop) =>
        new TransactionInput.RefundToClient(Date(valueDate), amountKop);

    private static BudgetPaymentInput Paid(PaymentKind kind, long amountKop, int month) =>
        new(kind, amountKop, 2026, new PaymentPeriod.Monthly(month));

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
