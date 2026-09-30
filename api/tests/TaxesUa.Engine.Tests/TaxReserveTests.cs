using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class TaxReserveTests
{
    private const long EsvMonthKop = 190_234;
    private const long EsvQuarterKop = 570_702;

    [Theory]
    [InlineData(500, 100, 1_000_000, 50_000, 10_000)]
    [InlineData(200, 150, 1_000_000, 20_000, 15_000)]
    [InlineData(500, 100, 10, 1, 0)]
    [InlineData(500, 100, 12_345, 617, 123)]
    [InlineData(0, 0, 1_000_000, 0, 0)]
    public void A_receipt_sets_aside_its_amount_times_the_rates_of_its_year(
        int singleTaxBp, int levyBp, long amountKop, long singleTaxKop, long levyKop)
    {
        var config = Config2026 with { SingleTaxRateBp = singleTaxBp, MilitaryLevyRateBp = levyBp };

        var setAside = TaxReserve.SetAsideFor(
            new TransactionInput.Income(Date("2026-02-10"), amountKop), config, Date("2026-01-01"));

        Assert.Equal(new SetAside(singleTaxKop, levyKop), setAside);
    }

    [Fact]
    public void A_non_income_kind_sets_aside_zero()
    {
        var transfer = new TransactionInput.NonIncome(
            Date("2026-02-10"), 1_000_000, NonIncomeKind.OwnTransfer, "own account");

        Assert.Equal(new SetAside(0, 0), TaxReserve.SetAsideFor(transfer, Config2026, Date("2026-01-01")));
    }

    [Fact]
    public void A_row_before_registration_shows_none_and_so_does_a_refund_of_such_a_receipt()
    {
        var registered = Date("2026-03-01");

        Assert.Null(TaxReserve.SetAsideFor(
            new TransactionInput.Income(Date("2026-02-10"), 1_000_000), Config2026, registered));
        Assert.Null(TaxReserve.SetAsideFor(
            new TransactionInput.RefundToClient(Date("2026-04-10"), 400_000, Date("2026-02-10")), Config2026, registered));
    }

    [Fact]
    public void A_refund_releases_reserve_at_the_rates_of_its_own_year()
    {
        var setAside = TaxReserve.SetAsideFor(
            new TransactionInput.RefundToClient(Date("2026-04-10"), 400_000, Date("2026-02-10")),
            Config2026,
            Date("2026-01-01"));

        Assert.Equal(new SetAside(-20_000, -4_000), setAside);
    }

    [Fact]
    public void Everything_accrued_and_unpaid_is_needed_grouped_by_due_date_oldest_first()
    {
        var need = Needed(Date("2026-04-25"), Receipts);

        Assert.Equal(
            [
                Due("2026-04-20", ObligationStatus.Overdue, esv: EsvQuarterKop),
                Due("2026-05-20", ObligationStatus.Upcoming, singleTax: 50_000, levy: 10_000),
                Due("2026-07-20", ObligationStatus.Upcoming, esv: EsvMonthKop),
            ],
            need.Dues);
        Assert.Equal(50_000 + 10_000 + EsvQuarterKop + EsvMonthKop, need.TotalKop);
    }

    [Fact]
    public void A_due_date_group_holds_its_kinds_apart_and_a_deadline_day_is_due_not_overdue()
    {
        var need = Needed(Date("2026-05-20"), Receipts);

        var due = Assert.Single(need.Dues, group => group.DueDate == Date("2026-05-20"));
        Assert.Equal((ObligationStatus.Due, 50_000L, 10_000L, 0L, 60_000L), (due.Status, due.SingleTaxKop, due.MilitaryLevyKop, due.EsvKop, due.TotalKop));
    }

    [Theory]
    [InlineData("2026-06-30", 2 * EsvQuarterKop)]
    [InlineData("2026-07-01", (2 * EsvQuarterKop) + EsvMonthKop)]
    [InlineData("2026-07-31", (2 * EsvQuarterKop) + EsvMonthKop)]
    [InlineData("2026-08-01", (2 * EsvQuarterKop) + (2 * EsvMonthKop))]
    public void ESV_of_the_current_quarter_counts_the_months_begun_and_a_new_quarter_starts_at_its_first_day(
        string today, long expectedEsvKop)
    {
        var need = Needed(Date(today), Receipts);

        Assert.Equal(expectedEsvKop, need.Dues.Sum(due => due.EsvKop));
        Assert.Equal(60_000L, need.Dues.Sum(due => due.SingleTaxKop + due.MilitaryLevyKop));
    }

    [Fact]
    public void The_current_quarters_tax_is_the_accrual_on_the_income_so_far()
    {
        var receipts = new TransactionInput[]
        {
            new TransactionInput.Income(Date("2026-02-10"), 1_000_000),
            new TransactionInput.Income(Date("2026-05-05"), 2_000_000),
        };

        var need = Needed(Date("2026-05-10"), receipts, Paid(PaymentKind.Esv, 5 * EsvQuarterKop, 2026, 1));

        Assert.Equal(
            [
                Due("2026-05-20", ObligationStatus.Upcoming, singleTax: 50_000, levy: 10_000),
                Due("2026-08-19", ObligationStatus.Upcoming, singleTax: 100_000, levy: 20_000),
            ],
            need.Dues);
    }

    [Fact]
    public void Payments_are_allocated_oldest_first_before_the_reserve_is_read()
    {
        var need = Needed(
            Date("2026-05-15"),
            Receipts,
            Paid(PaymentKind.Esv, 600_000, 2026, 2),
            Paid(PaymentKind.SingleTax, 20_000, 2026, 2));

        Assert.Equal(
            [
                Due("2026-05-20", ObligationStatus.Upcoming, singleTax: 30_000, levy: 10_000),
                Due("2026-07-20", ObligationStatus.Upcoming, esv: (2 * EsvMonthKop) - (600_000 - EsvQuarterKop)),
            ],
            need.Dues);
    }

    [Fact]
    public void Money_paid_ahead_of_a_quarter_not_yet_begun_does_not_reduce_what_is_needed_now()
    {
        var need = Needed(Date("2026-05-15"), Receipts, Paid(PaymentKind.Esv, 3 * EsvQuarterKop, 2026, 3));

        Assert.Equal(0L, need.Dues.Sum(due => due.EsvKop));
    }

    [Fact]
    public void Monthly_advances_change_nothing_because_the_mode_never_changes_a_balance()
    {
        var byQuarter = Needed(Date("2026-05-15"), Receipts, Paid(PaymentKind.Esv, EsvQuarterKop, 2026, 1));
        var byMonth = Needed(
            Date("2026-05-15"),
            Receipts,
            Advance(PaymentKind.Esv, EsvMonthKop, 1),
            Advance(PaymentKind.Esv, EsvMonthKop, 2),
            Advance(PaymentKind.Esv, EsvMonthKop, 3));

        Assert.Equal(byQuarter.Dues, byMonth.Dues);
        Assert.Equal(60_000 + (2 * EsvMonthKop), byMonth.TotalKop);
    }

    [Fact]
    public void A_refund_that_turns_a_quarter_negative_is_credit_against_the_older_debt()
    {
        var receipts = new TransactionInput[]
        {
            new TransactionInput.Income(Date("2026-02-10"), 1_000_000),
            new TransactionInput.RefundToClient(Date("2026-04-10"), 400_000),
        };

        var need = Needed(Date("2026-04-25"), receipts, Paid(PaymentKind.Esv, 5 * EsvQuarterKop, 2026, 1));

        Assert.Equal([Due("2026-05-20", ObligationStatus.Upcoming, singleTax: 30_000, levy: 6_000)], need.Dues);
    }

    [Fact]
    public void Nothing_is_needed_when_everything_accrued_is_paid()
    {
        var need = Needed(
            Date("2026-04-25"),
            Receipts,
            Paid(PaymentKind.SingleTax, 50_000, 2026, 1),
            Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1),
            Paid(PaymentKind.Esv, EsvQuarterKop + EsvMonthKop, 2026, 1));

        Assert.Empty(need.Dues);
        Assert.Equal(0L, need.TotalKop);
    }

    [Fact]
    public void An_exempt_owner_needs_no_ESV()
    {
        var exempt = Settings with { EsvExempt = true };

        var need = Needed(Date("2026-04-25"), Receipts, exempt, []);

        Assert.Equal(0L, need.Dues.Sum(due => due.EsvKop));
        Assert.Equal(60_000L, need.TotalKop);
    }

    [Fact]
    public void Each_year_is_read_against_its_own_months()
    {
        var years = new[]
        {
            Year(2025, [], Config2026, Settings with { FopRegistrationDate = Date("2025-01-01") }),
            Year(2026, Receipts, Config2026, Settings with { FopRegistrationDate = Date("2025-01-01") }),
        };
        var settings = Settings with { FopRegistrationDate = Date("2025-01-01") };
        var ledger = Balances.ForYears(years, settings, [], Date("2026-02-15"));

        var need = TaxReserve.Needed(ledger, years, Date("2026-02-15"));

        Assert.Equal((4 * EsvQuarterKop) + (2 * EsvMonthKop), need.Dues.Sum(due => due.EsvKop));
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

    private static readonly FopSettingsInput Settings = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: Date("2026-01-01"),
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static readonly TransactionInput[] Receipts =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

    private static ReserveNeed Needed(DateOnly today, TransactionInput[] receipts, params BudgetPaymentInput[] payments) =>
        Needed(today, receipts, Settings, payments);

    private static ReserveNeed Needed(
        DateOnly today, TransactionInput[] receipts, FopSettingsInput settings, BudgetPaymentInput[] payments)
    {
        var years = new[] { Year(2026, receipts, Config2026, settings) };
        return TaxReserve.Needed(Balances.ForYears(years, settings, payments, today), years, today);
    }

    private static LedgerYear Year(
        int year, TransactionInput[] transactions, TaxYearConfigInput config, FopSettingsInput settings) =>
        new(Accruals.ForYear(year, transactions, config, settings), config);

    private static ReserveDue Due(
        string date, ObligationStatus status, long singleTax = 0, long levy = 0, long esv = 0) =>
        new(Date(date), status, singleTax, levy, esv);

    private static BudgetPaymentInput Paid(PaymentKind kind, long amountKop, int year, int quarter) =>
        new(kind, amountKop, year, new PaymentPeriod.Quarterly(quarter));

    private static BudgetPaymentInput Advance(PaymentKind kind, long amountKop, int month) =>
        new(kind, amountKop, 2026, new PaymentPeriod.Monthly(month));

    private static DateOnly Date(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
