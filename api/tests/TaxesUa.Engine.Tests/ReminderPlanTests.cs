using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class ReminderPlanTests
{
    private const long EsvMonthKop = 190_234;

    private const long EsvQuarterKop = 3 * EsvMonthKop;

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
        LimitWarnThresholdsPct: [85, 100],
        Group3ApplicationDays: 10);

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static readonly TransactionInput[] OneReceipt =
        [new TransactionInput.Income(Date("2026-02-10"), 1_000_000)];

    // Q1 2026's ESV deadline is Sunday 19 April, shifted to Monday the 20th (Rule 5).
    [Theory]
    [InlineData("2026-04-13 08:59", null)]
    [InlineData("2026-04-13 09:00", ReminderOffset.WeekBefore)]
    [InlineData("2026-04-16 15:00", ReminderOffset.WeekBefore)]
    [InlineData("2026-04-19 08:59", ReminderOffset.WeekBefore)]
    [InlineData("2026-04-19 09:00", ReminderOffset.DayBefore)]
    [InlineData("2026-04-20 08:00", ReminderOffset.DayBefore)]
    [InlineData("2026-04-20 09:00", ReminderOffset.OnTheDay)]
    [InlineData("2026-04-20 23:59", ReminderOffset.OnTheDay)]
    [InlineData("2026-04-21 08:59", null)]
    [InlineData("2026-04-21 09:00", ReminderOffset.DayAfter)]
    [InlineData("2026-04-21 23:00", ReminderOffset.DayAfter)]
    [InlineData("2026-04-22 09:00", null)]
    public void A_deadline_is_reminded_at_the_latest_offset_whose_moment_has_passed(string now, ReminderOffset? expected)
    {
        var due = Due(now);

        if (expected is null)
        {
            Assert.Empty(due);
            return;
        }

        var reminder = Assert.Single(due);
        Assert.Equal((Date("2026-04-20"), expected.Value), (reminder.Date, reminder.Offset));
        Assert.Equal([Esv(2026, 1, EsvQuarterKop)], reminder.Items);
    }

    [Fact]
    public void The_week_before_counts_from_the_shifted_date_not_the_statutory_one()
    {
        Assert.Empty(Due("2026-04-12 09:00"));
        Assert.Equal(Date("2026-04-20"), Assert.Single(Due("2026-04-13 09:00")).Date);
    }

    [Fact]
    public void A_holiday_moves_the_reminders_with_the_deadline()
    {
        var withHoliday = Config2026 with { Holidays = [Date("2026-10-19")] };

        Assert.Empty(Due("2026-10-12 09:00", config: withHoliday));
        var reminder = Assert.Single(Due("2026-10-13 09:00", config: withHoliday));
        Assert.Equal((Date("2026-10-20"), ReminderOffset.WeekBefore), (reminder.Date, reminder.Offset));
        Assert.Equal([Esv(2026, 3, EsvQuarterKop)], reminder.Items);
    }

    [Theory]
    [InlineData("2026-04-13 09:00", "2026-04-20", 1)]
    [InlineData("2026-07-13 09:00", "2026-07-20", 2)]
    [InlineData("2026-10-12 09:00", "2026-10-19", 3)]
    [InlineData("2027-01-12 09:00", "2027-01-19", 4)]
    public void Every_quarter_has_its_esv_reminder_a_week_ahead_including_q4_in_the_next_year(
        string now, string deadline, int quarter)
    {
        var paidBefore = Enumerable.Range(1, quarter - 1)
            .Select(earlier => Paid(PaymentKind.Esv, EsvQuarterKop, 2026, earlier))
            .ToArray();

        var reminder = Assert.Single(Due(now, paidBefore), each => each.Items.Any(item => item is ReminderItem.Payment));

        Assert.Equal((Date(deadline), ReminderOffset.WeekBefore), (reminder.Date, reminder.Offset));
        Assert.Equal([Esv(2026, quarter, EsvQuarterKop)], reminder.Items);
    }

    [Fact]
    public void The_single_tax_and_the_levy_sharing_a_date_come_in_one_reminder_with_two_amounts()
    {
        var reminder = Assert.Single(Due("2026-05-13 09:00"));

        Assert.Equal(Date("2026-05-20"), reminder.Date);
        Assert.Equal([SingleTax(2026, 1, 50_000), Levy(2026, 1, 10_000)], reminder.Items);
        Assert.Equal(ReminderKinds.SingleTax | ReminderKinds.MilitaryLevy, reminder.Kinds);
    }

    [Fact]
    public void A_kind_paid_in_full_is_left_out_and_a_date_with_nothing_owed_has_no_reminder()
    {
        var levyPaid = Assert.Single(Due("2026-05-13 09:00", [Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1)]));
        Assert.Equal([SingleTax(2026, 1, 50_000)], levyPaid.Items);

        Assert.Empty(Due(
            "2026-05-13 09:00",
            [Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1), Paid(PaymentKind.SingleTax, 50_000, 2026, 1)]));
    }

    [Fact]
    public void A_partly_paid_kind_is_reminded_of_what_is_left()
    {
        var reminder = Assert.Single(Due("2026-05-20 09:00", [Paid(PaymentKind.SingleTax, 20_000, 2026, 1)]));

        Assert.Equal(ReminderOffset.OnTheDay, reminder.Offset);
        Assert.Equal([SingleTax(2026, 1, 30_000), Levy(2026, 1, 10_000)], reminder.Items);
    }

    [Fact]
    public void A_payment_named_for_a_later_quarter_settles_the_oldest_first_so_that_one_is_not_reminded()
    {
        Assert.Empty(Due("2026-04-13 09:00", [Paid(PaymentKind.Esv, EsvQuarterKop, 2026, 2)]));

        var q2 = Assert.Single(Due("2026-07-13 09:00", [Paid(PaymentKind.Esv, EsvQuarterKop, 2026, 2)]));
        Assert.Equal([Esv(2026, 2, EsvQuarterKop)], q2.Items);
    }

    [Fact]
    public void The_day_after_is_only_for_a_payment_still_owed()
    {
        var overdue = Assert.Single(Due("2026-05-21 09:00", [Paid(PaymentKind.SingleTax, 50_000, 2026, 1)]));
        Assert.Equal((Date("2026-05-20"), ReminderOffset.DayAfter), (overdue.Date, overdue.Offset));
        Assert.Equal([Levy(2026, 1, 10_000)], overdue.Items);

        Assert.Empty(Due(
            "2026-05-21 09:00",
            [Paid(PaymentKind.SingleTax, 50_000, 2026, 1), Paid(PaymentKind.MilitaryLevy, 10_000, 2026, 1)]));
    }

    [Theory]
    [InlineData("2026-05-04 09:00", ReminderOffset.WeekBefore)]
    [InlineData("2026-05-10 09:00", ReminderOffset.DayBefore)]
    [InlineData("2026-05-11 09:00", ReminderOffset.OnTheDay)]
    public void A_declaration_not_marked_filed_is_reminded_until_its_deadline(string now, ReminderOffset expected)
    {
        var reminder = Assert.Single(Due(now));

        Assert.Equal((Date("2026-05-11"), expected), (reminder.Date, reminder.Offset));
        Assert.Equal([new ReminderItem.Declaration(2026, 1)], reminder.Items);
        Assert.Equal(ReminderKinds.Declaration, reminder.Kinds);
    }

    [Fact]
    public void A_declaration_is_not_reminded_the_day_after()
    {
        Assert.Empty(Due("2026-05-12 09:00"));
    }

    [Fact]
    public void A_declaration_marked_filed_is_not_reminded()
    {
        Assert.Empty(Due("2026-05-04 09:00", filed: [new YearQuarter(2026, 1)]));
        Assert.Single(Due("2026-08-03 09:00", filed: [new YearQuarter(2026, 1)]));
    }

    [Fact]
    public void A_quarter_that_ended_before_registration_has_no_declaration_to_remind()
    {
        var registeredInMay = Settings(Date("2026-05-15"));

        Assert.Empty(Due("2026-05-04 09:00", settings: registeredInMay));
        Assert.Equal(
            [new ReminderItem.Declaration(2026, 2)],
            Assert.Single(Due("2026-08-03 09:00", settings: registeredInMay)).Items);
    }

    [Fact]
    public void Nothing_is_reminded_for_quarters_after_the_limit_crossing()
    {
        TransactionInput[] overTheLimit = [new TransactionInput.Income(Date("2026-02-10"), 1_100_000_000)];

        Assert.NotEmpty(Due("2026-04-13 09:00", receipts: overTheLimit));
        Assert.NotEmpty(Due("2026-05-04 09:00", receipts: overTheLimit));
        Assert.Empty(Due("2026-07-13 09:00", receipts: overTheLimit));
        Assert.Empty(Due("2026-08-03 09:00", receipts: overTheLimit));
        Assert.Empty(Due("2026-08-12 09:00", receipts: overTheLimit));
    }

    [Fact]
    public void An_advance_is_reminded_at_its_recommended_date_in_monthly_advance_mode()
    {
        var january = Assert.Single(Due("2026-02-08 09:00", advances: true));
        Assert.Equal((Date("2026-02-15"), ReminderOffset.WeekBefore), (january.Date, january.Offset));
        Assert.Equal([Advance(PaymentKind.Esv, 1, EsvMonthKop)], january.Items);

        Assert.Empty(Due("2026-02-08 09:00", advances: false));
    }

    [Fact]
    public void An_unpaid_earlier_advance_is_carried_into_the_next_one()
    {
        var february = Assert.Single(Due("2026-03-08 09:00", advances: true));

        Assert.Equal(
            [
                Advance(PaymentKind.SingleTax, 2, 50_000),
                Advance(PaymentKind.MilitaryLevy, 2, 10_000),
                Advance(PaymentKind.Esv, 2, 2 * EsvMonthKop),
            ],
            february.Items);
    }

    [Fact]
    public void A_paid_advance_is_not_reminded_and_an_advance_is_never_overdue()
    {
        Assert.Empty(Due("2026-02-08 09:00", [Paid(PaymentKind.Esv, EsvMonthKop, 2026, 1)], advances: true));
        Assert.Empty(Due("2026-02-16 09:00", advances: true));
    }

    [Fact]
    public void The_quarterly_deadline_is_still_reminded_in_advance_mode_after_the_quarters_last_advance()
    {
        var due = Due("2026-04-13 09:00", advances: true);

        Assert.Equal([Date("2026-04-15"), Date("2026-04-20")], due.Select(reminder => reminder.Date));
        Assert.Equal(
            [
                Advance(PaymentKind.SingleTax, 3, 50_000),
                Advance(PaymentKind.MilitaryLevy, 3, 10_000),
                Advance(PaymentKind.Esv, 3, EsvQuarterKop),
            ],
            due[0].Items);
        Assert.Equal([Esv(2026, 1, EsvQuarterKop)], due[1].Items);
    }

    private static IReadOnlyList<Reminder> Due(
        string now,
        BudgetPaymentInput[]? payments = null,
        YearQuarter[]? filed = null,
        TaxYearConfigInput? config = null,
        FopSettingsInput? settings = null,
        TransactionInput[]? receipts = null,
        bool advances = false)
    {
        var moment = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(moment);
        var fop = settings ?? RegisteredIn2025;
        var yearConfig = config ?? Config2026;
        var year = new LedgerYear(Accruals.ForYear(2026, receipts ?? OneReceipt, yearConfig, fop), yearConfig);
        var ledger = Balances.ForYears([year], fop, payments ?? [], today);

        return ReminderPlan.Due(
            [year],
            fop,
            ledger,
            advances ? MonthlyAdvances.ForYear(year.Accrual, ledger, recommendedDay: 15) : null,
            new HashSet<YearQuarter>(filed ?? []),
            today,
            TimeOnly.FromDateTime(moment));
    }

    private static ReminderItem.Payment Esv(int year, int quarter, long amountKop) =>
        new(PaymentKind.Esv, year, quarter, null, amountKop);

    private static ReminderItem.Payment SingleTax(int year, int quarter, long amountKop) =>
        new(PaymentKind.SingleTax, year, quarter, null, amountKop);

    private static ReminderItem.Payment Levy(int year, int quarter, long amountKop) =>
        new(PaymentKind.MilitaryLevy, year, quarter, null, amountKop);

    private static ReminderItem.Payment Advance(PaymentKind kind, int month, long amountKop) =>
        new(kind, 2026, (month + 2) / 3, month, amountKop);

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
