using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class EsvAnnexTests
{
    private const long MinWageKop = 864_700;
    private const long EsvMonthKop = 190_234;

    [Fact]
    public void A_full_year_reports_twelve_months_of_the_minimum_wage_on_the_annual_declaration()
    {
        var year = Accruals.ForYear(2026, [], Config2026, Settings(Date("2025-06-10")));

        var annex = year.EsvAnnex!;

        Assert.Equal((2026, 4, Date("2026-01-01"), Date("2026-12-31"), false), (annex.Year, annex.Quarter, annex.From, annex.To, annex.LeavesGroup3));
        Assert.Equal(Enumerable.Range(1, 12), annex.Months.Select(month => month.Month));
        Assert.All(annex.Months, month => Assert.Equal((MinWageKop, 2_200, EsvMonthKop), (month.BaseKop, month.RateBp, month.EsvKop)));
        Assert.Equal((12 * MinWageKop, 12 * EsvMonthKop), (annex.BaseKop, annex.EsvKop));
        Assert.Equal(year.Quarters.Sum(quarter => quarter.EsvKop), annex.EsvKop);
    }

    [Theory]
    [InlineData(EsvRegistrationMonthPolicy.Prorated, 613_658, 135_005)]
    [InlineData(EsvRegistrationMonthPolicy.FullMonth, MinWageKop, EsvMonthKop)]
    public void A_first_year_starts_at_registration_with_its_month_in_full_or_under_the_prorated_option_its_active_days(
        EsvRegistrationMonthPolicy policy, long marchBaseKop, long marchEsvKop)
    {
        var settings = Settings(Date("2026-03-10")) with { EsvRegistrationMonthPolicy = policy };
        var year = Accruals.ForYear(2026, [], Config2026, settings);

        var annex = year.EsvAnnex!;

        Assert.Equal((Date("2026-03-10"), Date("2026-12-31")), (annex.From, annex.To));
        Assert.Equal(Enumerable.Range(3, 10), annex.Months.Select(month => month.Month));
        Assert.Equal((marchBaseKop, marchEsvKop), (annex.Months[0].BaseKop, annex.Months[0].EsvKop));
        Assert.Equal(year.Quarters.Sum(quarter => quarter.EsvKop), annex.EsvKop);
    }

    [Fact]
    public void Registering_on_the_28th_of_september_owes_the_full_month_by_default_and_a_tenth_of_it_when_prorated()
    {
        var full = Accruals.ForYear(2026, [], Config2026, Settings(Date("2026-09-28")));
        var prorated = Accruals.ForYear(2026, [], Config2026,
            Settings(Date("2026-09-28")) with { EsvRegistrationMonthPolicy = EsvRegistrationMonthPolicy.Prorated });

        var september = full.EsvAnnex!.Months[0];

        Assert.Equal((9, MinWageKop, EsvMonthKop), (september.Month, september.BaseKop, september.EsvKop));
        Assert.Equal(190_234, Q3EsvKop(full));
        Assert.Equal(760_936, full.EsvAnnex.EsvKop);
        Assert.Equal((86_470, 19_023), (prorated.EsvAnnex!.Months[0].BaseKop, Q3EsvKop(prorated)));
        Assert.Equal(171_211, Q3EsvKop(full) - Q3EsvKop(prorated));
    }

    // Base × rate, rounded once each; prorating the month's ESV instead gave 164_869 and 120_482.
    [Theory]
    [InlineData("2026-04-05", 164_870)]
    [InlineData("2026-04-12", 120_481)]
    public void Under_the_prorated_policy_the_registration_month_esv_is_the_prorated_base_at_the_rate(string registered, long aprilEsvKop)
    {
        var settings = Settings(Date(registered)) with { EsvRegistrationMonthPolicy = EsvRegistrationMonthPolicy.Prorated };
        var year = Accruals.ForYear(2026, [], Config2026, settings);

        var april = year.EsvAnnex!.Months[0];

        Assert.Equal((4, aprilEsvKop), (april.Month, april.EsvKop));
        Assert.Equal(aprilEsvKop, year.Months.Single(month => month.Month == 4).EsvKop);
    }

    [Fact]
    public void The_crossing_quarter_carries_the_annex_for_the_months_before_the_switch()
    {
        var year = Accruals.ForYear(2026, CrossedInQ3, Config2026, Settings(Date("2025-01-01")));

        var annex = year.EsvAnnex!;

        Assert.Equal((3, Date("2026-01-01"), Date("2026-09-30"), true), (annex.Quarter, annex.From, annex.To, annex.LeavesGroup3));
        Assert.Equal(9 * EsvMonthKop, annex.EsvKop);
        Assert.Same(annex, Declaration.ForQuarter(year, 3).EsvAnnex);
        Assert.Equal(9 * EsvMonthKop, Declaration.ForQuarter(year, 3).EsvKop);
        Assert.Null(Declaration.ForQuarter(year, 2).EsvAnnex);
    }

    [Fact]
    public void A_crossing_in_Q4_is_still_the_annual_annex_and_marks_the_switch()
    {
        var year = Accruals.ForYear(2026, [Income("2026-11-10", 1_100_000_000)], Config2026, Settings(Date("2025-01-01")));

        var annex = year.EsvAnnex!;

        Assert.Equal((4, 12, true), (annex.Quarter, annex.Months.Count, annex.LeavesGroup3));
    }

    [Fact]
    public void A_return_to_group_3_starts_the_annex_at_the_quarter_of_the_return()
    {
        var settings = Settings(Date("2025-01-01")) with { BackOnGroup3From = new YearQuarter(2026, 3) };
        var years = Accruals.ForYears(
            [
                new AccrualYearInput(2025, [Income("2025-11-10", 1_100_000_000)], Config2026),
                new AccrualYearInput(2026, [], Config2026),
            ],
            settings);

        var annex = years[1].EsvAnnex!;

        Assert.Equal((4, Date("2026-07-01"), Date("2026-12-31"), false), (annex.Quarter, annex.From, annex.To, annex.LeavesGroup3));
        Assert.Equal(Enumerable.Range(7, 6), annex.Months.Select(month => month.Month));
    }

    [Fact]
    public void An_esv_exemption_has_no_annex_and_no_line_21()
    {
        var year = Accruals.ForYear(2026, [], Config2026, Settings(Date("2025-01-01")) with { EsvExempt = true });

        Assert.Null(year.EsvAnnex);
        Assert.Null(Declaration.ForQuarter(year, 4).EsvKop);
    }

    [Fact]
    public void Without_a_registration_date_there_is_no_annex()
    {
        Assert.Null(Accruals.ForYear(2026, [], Config2026, Settings(null)).EsvAnnex);
    }

    private static readonly TransactionInput[] CrossedInQ3 =
    [
        Income("2026-02-10", 400_000_000),
        Income("2026-05-10", 400_000_000),
        Income("2026-08-10", 250_000_000),
    ];

    private static readonly TaxYearConfigInput Config2026 = new(
        MinWageKop: MinWageKop,
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

    private static long Q3EsvKop(YearAccrual year) => year.Quarters.Single(quarter => quarter.Income.Quarter == 3).EsvKop;

    private static FopSettingsInput Settings(DateOnly? registrationDate) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static TransactionInput Income(string iso, long amountKop) => new TransactionInput.Income(Date(iso), amountKop);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
