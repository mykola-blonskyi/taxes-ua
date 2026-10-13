namespace TaxesUa.Engine.Tests;

public class DocumentRetentionTests
{
    private static readonly TaxYearConfigInput ReferenceConfig = new(
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

    private static readonly FopSettingsInput ReferenceSettings = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: null,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static KeepUntil? ForYear(LimitationSuspension? suspension, params RetainedDeclaration[] declarations) =>
        DocumentRetention.ForYear(2026, declarations, ReferenceConfig, ReferenceSettings, suspension);

    [Fact]
    public void An_unfiled_declaration_counts_from_its_shifted_deadline()
    {
        // Q1 2026's statutory deadline, 2026-05-10, is a Sunday; it is due Monday the 11th.
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2029, 5, 10)),
            ForYear(null, new RetainedDeclaration(1, null)));
    }

    [Fact]
    public void A_filed_declaration_counts_from_the_day_it_was_filed()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2029, 4, 20)),
            ForYear(null, new RetainedDeclaration(1, new DateOnly(2026, 4, 21))));
    }

    [Fact]
    public void A_declaration_filed_late_counts_from_the_later_filing_day()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2029, 6, 30)),
            ForYear(null, new RetainedDeclaration(1, new DateOnly(2026, 7, 1))));
    }

    [Fact]
    public void The_latest_quarter_decides_the_year()
    {
        // Q4 is due 2027-02-09; Q2 filed late in 2027 still ends earlier.
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2030, 2, 8)),
            ForYear(
                null,
                new RetainedDeclaration(1, new DateOnly(2026, 4, 21)),
                new RetainedDeclaration(2, new DateOnly(2027, 1, 15)),
                new RetainedDeclaration(3, null),
                new RetainedDeclaration(4, null)));
    }

    [Fact]
    public void A_year_without_a_group_3_declaration_has_no_date()
    {
        Assert.Null(ForYear(new LimitationSuspension(new DateOnly(2022, 3, 17), null)));
    }

    [Fact]
    public void The_last_day_kept_is_day_1095_counted_from_the_day_after()
    {
        var countsFrom = new DateOnly(2026, 1, 1);

        var until = Assert.IsType<KeepUntil.On>(DocumentRetention.ForDeclaration(countsFrom, null));

        Assert.Equal(1095, until.Date.DayNumber - countsFrom.DayNumber);
        Assert.Equal(new DateOnly(2028, 12, 31), until.Date);
    }

    [Fact]
    public void A_count_that_ends_the_day_before_a_suspension_is_not_extended()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2028, 12, 31)),
            DocumentRetention.ForDeclaration(
                new DateOnly(2026, 1, 1), new LimitationSuspension(new DateOnly(2029, 1, 1), null)));
    }

    [Fact]
    public void A_suspension_starting_on_day_1095_leaves_one_day_after_it_ends()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2030, 7, 1)),
            DocumentRetention.ForDeclaration(
                new DateOnly(2026, 1, 1),
                new LimitationSuspension(new DateOnly(2028, 12, 31), new DateOnly(2030, 6, 30))));
    }

    [Fact]
    public void An_open_suspension_stops_the_count_for_as_long_as_it_lasts()
    {
        Assert.Equal(
            new KeepUntil.WhileSuspended(1095, new DateOnly(2029, 5, 10)),
            ForYear(new LimitationSuspension(new DateOnly(2022, 3, 17), null), new RetainedDeclaration(1, null)));
    }

    [Fact]
    public void An_open_suspension_that_starts_mid_count_keeps_the_days_counted_before_it()
    {
        // Days 1 to 30 run 2026-01-02 to 2026-01-31; the remaining 1065 wait for the end.
        Assert.Equal(
            new KeepUntil.WhileSuspended(1065, new DateOnly(2028, 12, 31)),
            DocumentRetention.ForDeclaration(
                new DateOnly(2026, 1, 1), new LimitationSuspension(new DateOnly(2026, 2, 1), null)));
    }

    [Fact]
    public void A_closed_suspension_extends_the_count_by_the_suspended_days()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2030, 12, 31).AddDays(1065)),
            DocumentRetention.ForDeclaration(
                new DateOnly(2026, 1, 1),
                new LimitationSuspension(new DateOnly(2026, 2, 1), new DateOnly(2030, 12, 31))));
    }

    [Fact]
    public void A_count_inside_a_closed_suspension_starts_after_it_ends()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2027, 6, 30).AddDays(1095)),
            ForYear(
                new LimitationSuspension(new DateOnly(2022, 3, 17), new DateOnly(2027, 6, 30)),
                new RetainedDeclaration(1, null)));
    }

    [Fact]
    public void A_count_starting_on_the_last_suspended_day_is_not_extended()
    {
        Assert.Equal(
            new KeepUntil.On(new DateOnly(2029, 5, 10)),
            ForYear(
                new LimitationSuspension(new DateOnly(2022, 3, 17), new DateOnly(2026, 5, 11)),
                new RetainedDeclaration(1, null)));
    }

    [Fact]
    public void A_year_with_one_quarter_still_suspended_is_extended()
    {
        var suspension = new LimitationSuspension(new DateOnly(2026, 6, 1), null);

        Assert.Equal(
            new KeepUntil.WhileSuspended(1095, new DateOnly(2030, 2, 8)),
            ForYear(
                suspension,
                new RetainedDeclaration(1, new DateOnly(2023, 1, 1)),
                new RetainedDeclaration(4, null)));
    }
}
