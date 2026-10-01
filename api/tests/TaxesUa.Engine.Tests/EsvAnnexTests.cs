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
    public void A_first_year_starts_at_registration_and_prorates_the_base_of_its_month(
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

    private static FopSettingsInput Settings(DateOnly? registrationDate) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.Prorated,
        EsvExempt: false);

    private static TransactionInput Income(string iso, long amountKop) => new TransactionInput.Income(Date(iso), amountKop);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
