using System.Globalization;

namespace TaxesUa.Engine.Tests;

/// <summary>
/// Tax Code 298.1.4: registered on 2026-09-28 without a timely group 3 application, the FOP is on the
/// general system until group 3 starts on 2027-01-01, so 2026 after registration has no group 3 quarter
/// and its income is reported apart. ESV does not depend on the tax system, so it accrues from
/// registration all the same.
/// </summary>
public class Group3StartTests
{
    private const long EsvMonthKop = 190_234;

    private static readonly FopSettingsInput Late = Settings(Date("2026-09-28"), group3Since: Date("2027-01-01"));

    private static readonly TransactionInput[] Operations =
    [
        Income("2026-08-15", 700_000),
        Income("2026-10-10", 1_000_000),
        Income("2026-12-05", 2_000_000),
        new TransactionInput.RefundToClient(Date("2026-12-20"), 300_000, Date("2026-12-05")),
        Income("2027-02-10", 4_000_000),
        new TransactionInput.RefundToClient(Date("2027-03-01"), 500_000, Date("2026-12-05")),
        new TransactionInput.RefundToClient(Date("2027-03-02"), 100_000, Date("2027-02-10")),
    ];

    [Fact]
    public void Group3_starts_on_the_later_of_registration_and_group3_since()
    {
        Assert.Equal(Date("2027-01-01"), Late.Group3Start);
        Assert.Equal(Date("2026-09-28"), Settings(Date("2026-09-28")).Group3Start);
        Assert.Equal(Date("2026-09-28"), Settings(Date("2026-09-28"), Date("2026-09-28")).Group3Start);
        Assert.Null(Settings(null, Date("2027-01-01")).Group3Start);
    }

    [Fact]
    public void The_registration_year_has_no_group_3_quarter_after_registration_and_reports_its_income_apart()
    {
        var (year2026, _) = Years(Late);

        Assert.Equal(
            [(1, true, 0L, 0L, 0L), (2, true, 0L, 0L, 0L), (3, false, 0L, 0L, EsvMonthKop), (4, false, 0L, 0L, 3 * EsvMonthKop)],
            year2026.Quarters.Select(quarter => (quarter.Income.Quarter, quarter.Group3, quarter.SingleTaxKop, quarter.MilitaryLevyKop, quarter.EsvKop)));
        Assert.False(year2026.InGroup3(3));
        Assert.False(year2026.InGroup3(4));
        Assert.True(year2026.Accrues(PaymentKind.Esv, 4));
        Assert.False(year2026.Accrues(PaymentKind.SingleTax, 4));
        Assert.Equal(
            [(9, 0L, 0L, EsvMonthKop), (10, 0L, 0L, EsvMonthKop)],
            year2026.Months.Where(month => month.Month is 9 or 10).Select(month => (month.Month, month.SingleTaxKop, month.MilitaryLevyKop, month.EsvKop)));
        Assert.Equal(0, year2026.Income.TotalIncomeKop);
        Assert.Equal(new BeforeGroup3(Date("2026-09-28"), Date("2026-12-31"), 2_700_000), year2026.Income.BeforeGroup3);
        Assert.Equal(
            [new EngineWarning.OperationBeforeRegistration(Date("2026-08-15"), Date("2026-09-28"))],
            year2026.Warnings);
        Assert.Null(year2026.LimitCrossing);
        Assert.Null(year2026.StoppedAtYearEnd);
        Assert.Null(year2026.EsvAnnex);
    }

    [Fact]
    public void The_first_group_3_year_accrues_from_zero_and_leaves_out_a_refund_of_an_earlier_receipt()
    {
        var (_, year2027) = Years(Late);

        Assert.Null(year2027.Income.BeforeGroup3);
        var q1 = year2027.QuarterOf(1);
        Assert.Equal(new QuarterIncome(1, 3_900_000, 3_900_000), q1.Income);
        Assert.Equal(195_000, q1.CumulativeSingleTaxKop);
        Assert.Equal(39_000, q1.CumulativeMilitaryLevyKop);
        Assert.Equal(3 * EsvMonthKop, q1.EsvKop);
        Assert.Equal(Date("2027-01-01"), year2027.EsvAnnex!.From);
        Assert.Empty(year2027.Warnings);
    }

    [Fact]
    public void Group3_from_the_registration_date_changes_nothing()
    {
        var unset = Years(Settings(Date("2026-09-28")));
        var same = Years(Settings(Date("2026-09-28"), Date("2026-09-28")));

        foreach (var (before, after) in new[] { (unset.Year2026, same.Year2026), (unset.Year2027, same.Year2027) })
        {
            Assert.Null(after.Income.BeforeGroup3);
            Assert.Equal(before.Quarters, after.Quarters);
            Assert.Equal(before.Months, after.Months);
            Assert.Equal(before.Warnings, after.Warnings);
            Assert.Equal(
                (before.EsvAnnex?.From, before.EsvAnnex?.To, before.EsvAnnex?.EsvKop),
                (after.EsvAnnex?.From, after.EsvAnnex?.To, after.EsvAnnex?.EsvKop));
        }

        Assert.True(same.Year2026.InGroup3(3));
        Assert.Equal(Date("2026-09-28"), same.Year2026.EsvAnnex!.From);
    }

    [Fact]
    public void Esv_naming_a_quarter_before_group_3_is_allocated_and_its_single_tax_is_outside_group_3()
    {
        var (year2026, year2027) = Years(Late);
        var esvBefore = new BudgetPaymentInput(PaymentKind.Esv, 3 * EsvMonthKop, 2026, new PaymentPeriod.Quarterly(4));
        var taxBefore = new BudgetPaymentInput(PaymentKind.SingleTax, 10_000, 2026, new PaymentPeriod.Quarterly(4));
        var esvInGroup3 = new BudgetPaymentInput(PaymentKind.Esv, 3 * EsvMonthKop, 2027, new PaymentPeriod.Quarterly(1));

        var ledger = Balances.ForYears(
            [new LedgerYear(year2026, Config), new LedgerYear(year2027, Config)],
            Late,
            [esvBefore, taxBefore, esvInGroup3],
            Date("2027-05-01"));

        Assert.Empty(ledger.Esv.OutsideGroup3);
        Assert.Equal([esvBefore, esvInGroup3], ledger.Esv.Payments);
        Assert.Equal(
            [(3, EsvMonthKop, 0L), (4, 3 * EsvMonthKop, 0L)],
            ledger.Esv.Obligations.Where(o => o.Year == 2026 && o.Quarter >= 3).Select(o => (o.Quarter, o.AccruedKop, o.RemainingKop)));
        Assert.Equal([taxBefore], ledger.SingleTax.OutsideGroup3);
        Assert.All(ledger.SingleTax.Obligations.Where(o => o.Year == 2026), o => Assert.Equal(0, o.AccruedKop));
    }

    [Fact]
    public void Esv_of_a_quarter_before_group_3_is_reminded()
    {
        var due = Due(Late, "2026-10-19 09:00");

        var reminder = Assert.Single(due);
        Assert.Equal(ReminderOffset.OnTheDay, reminder.Offset);
        Assert.Equal([new ReminderItem.Payment(PaymentKind.Esv, 2026, 3, null, EsvMonthKop)], reminder.Items);
    }

    [Theory]
    [InlineData("2026-11-09 09:00")]
    [InlineData("2027-02-09 09:00")]
    public void No_declaration_is_reminded_for_a_quarter_before_group_3(string now)
    {
        var paidUp = new[]
        {
            new BudgetPaymentInput(PaymentKind.Esv, 4 * EsvMonthKop, 2026, new PaymentPeriod.Quarterly(3)),
        };

        Assert.Empty(Due(Late, now, paidUp));
    }

    [Fact]
    public void Group3_from_the_next_quarter_accrues_the_registration_months_esv_and_files_it_in_the_q4_annex()
    {
        var settings = Settings(Date("2026-09-28"), Date("2026-10-01"));

        var year = Accruals.ForYear(
            2026, [Income("2026-09-29", 500_000), Income("2026-10-10", 1_000_000)], Config, settings);

        var q3 = year.Quarters.Single(quarter => quarter.Income.Quarter == 3);
        Assert.Equal((false, 0L, 0L, 190_234L), (q3.Group3, q3.SingleTaxKop, q3.MilitaryLevyKop, q3.EsvKop));
        Assert.False(year.InGroup3(3));
        Assert.True(year.InGroup3(4));
        Assert.Equal(new BeforeGroup3(Date("2026-09-28"), Date("2026-09-30"), 500_000), year.Income.BeforeGroup3);
        Assert.Equal(new QuarterIncome(4, 1_000_000, 1_000_000), year.QuarterOf(4).Income);
        var annex = year.EsvAnnex!;
        Assert.Equal((4, Date("2026-09-28"), Date("2026-12-31")), (annex.Quarter, annex.From, annex.To));
        Assert.Equal([9, 10, 11, 12], annex.Months.Select(month => month.Month));
        Assert.Equal(190_234, annex.Months[0].EsvKop);
    }

    [Fact]
    public void Nothing_is_set_aside_from_income_before_group_3()
    {
        var start = Late.Group3Start!.Value;

        Assert.Null(TaxReserve.SetAsideFor(Income("2026-10-10", 1_000_000), Config, start));
        Assert.Null(TaxReserve.SetAsideFor(Operations[5], Config, start));
        Assert.Equal(new SetAside(200_000, 40_000), TaxReserve.SetAsideFor(Income("2027-02-10", 4_000_000), Config, start));
    }

    private static IReadOnlyList<Reminder> Due(FopSettingsInput settings, string now, BudgetPaymentInput[]? payments = null)
    {
        var (year2026, year2027) = Years(settings);
        LedgerYear[] years = [new(year2026, Config), new(year2027, Config)];
        var moment = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(moment);
        return ReminderPlan.Due(
            years, settings, Balances.ForYears(years, settings, payments ?? [], today), null, new HashSet<YearQuarter>(), today, TimeOnly.FromDateTime(moment));
    }

    private static (YearAccrual Year2026, YearAccrual Year2027) Years(FopSettingsInput settings)
    {
        var years = Accruals.ForYears(
            [new AccrualYearInput(2026, Operations, Config), new AccrualYearInput(2027, Operations, Config)],
            settings);
        return (years[0], years[1]);
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

    private static FopSettingsInput Settings(DateOnly? registrationDate, DateOnly? group3Since = null) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false,
        Group3Since: group3Since);

    private static TransactionInput Income(string date, long amountKop) =>
        new TransactionInput.Income(Date(date), amountKop);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
