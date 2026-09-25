using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class DeadlineCalendarTests
{
    private static readonly TaxYearConfigInput ReferenceConfig = new(
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: []);

    private static readonly FopSettingsInput ReferenceSettings = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true);

    [Theory]
    [InlineData(1, "2026-04-20", "2026-05-11", "2026-05-20")]
    [InlineData(2, "2026-07-20", "2026-08-10", "2026-08-19")]
    [InlineData(3, "2026-10-19", "2026-11-09", "2026-11-19")]
    [InlineData(4, "2027-01-19", "2027-02-09", "2027-02-19")]
    public void Reproduces_the_2026_reference_table(
        int quarter,
        string esv,
        string declaration,
        string taxPayment)
    {
        var actual = DeadlineCalendar.ForQuarter(2026, quarter, ReferenceConfig, ReferenceSettings);

        Assert.Equal(
            (Date(esv), Date(declaration), Date(taxPayment)),
            (actual.Esv.Due, actual.Declaration.Due, actual.TaxPayment.Due));
    }

    [Fact]
    public void Q4_deadlines_fall_in_the_following_year()
    {
        var actual = DeadlineCalendar.ForQuarter(2026, 4, ReferenceConfig, ReferenceSettings);

        Assert.Equal(
            (2027, 2027, 2027),
            (actual.Esv.Due.Year, actual.Declaration.Due.Year, actual.TaxPayment.Due.Year));
    }

    [Theory]
    [InlineData(true, "2026-05-20")]
    [InlineData(false, "2026-05-21")]
    public void Tax_payment_counts_from_the_statutory_or_the_shifted_declaration_date(
        bool fromStatutory,
        string expectedDue)
    {
        var settings = ReferenceSettings with
        {
            TaxPaymentCountsFromStatutoryDeclarationDate = fromStatutory,
        };

        var actual = DeadlineCalendar.ForQuarter(2026, 1, ReferenceConfig, settings);

        Assert.Equal(Date("2026-05-10"), actual.Declaration.Statutory);
        Assert.Equal(Date("2026-05-11"), actual.Declaration.Due);
        Assert.Equal(Date(expectedDue), actual.TaxPayment.Due);
    }

    [Theory]
    [InlineData(true, "2026-05-25")]
    [InlineData(false, "2026-05-23")]
    public void Tax_payment_shifts_off_a_weekend_only_when_configured_to(
        bool shift,
        string expectedDue)
    {
        var config = ReferenceConfig with { TaxPaymentDaysAfterDeclaration = 13 };
        var settings = ReferenceSettings with { ShiftTaxPaymentFromWeekend = shift };

        var actual = DeadlineCalendar.ForQuarter(2026, 1, config, settings);

        Assert.Equal(Date("2026-05-23"), actual.TaxPayment.Statutory);
        Assert.Equal(Date(expectedDue), actual.TaxPayment.Due);
    }

    [Theory]
    [InlineData(3, "2026-10-19", "2026-10-19", "2026-10-20")]
    [InlineData(1, "2026-04-20", "2026-04-19", "2026-04-21")]
    public void A_configured_holiday_moves_the_esv_deadline(
        int quarter,
        string holiday,
        string expectedStatutory,
        string expectedDue)
    {
        var config = ReferenceConfig with { Holidays = [Date(holiday)] };

        var actual = DeadlineCalendar.ForQuarter(2026, quarter, config, ReferenceSettings);

        Assert.Equal(Date(expectedStatutory), actual.Esv.Statutory);
        Assert.Equal(Date(expectedDue), actual.Esv.Due);
    }

    [Fact]
    public void A_configured_holiday_moves_the_declaration_deadline()
    {
        var config = ReferenceConfig with { Holidays = [Date("2026-11-09")] };

        var actual = DeadlineCalendar.ForQuarter(2026, 3, config, ReferenceSettings);

        Assert.Equal(Date("2026-11-09"), actual.Declaration.Statutory);
        Assert.Equal(Date("2026-11-10"), actual.Declaration.Due);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void An_out_of_range_quarter_is_rejected(int quarter) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeadlineCalendar.ForQuarter(2026, quarter, ReferenceConfig, ReferenceSettings));

    [Fact]
    public void A_week_with_no_business_day_is_rejected()
    {
        var settings = ReferenceSettings with { WeekendDays = Enum.GetValues<DayOfWeek>() };

        Assert.Throws<ArgumentException>(() =>
            DeadlineCalendar.ForQuarter(2026, 1, ReferenceConfig, settings));
    }

    [Theory]
    [InlineData(1, 20, 41, "2026-04-20", "2026-05-11", "2026-05-11")]
    [InlineData(3, 21, 45, "2026-10-21", "2026-11-14", "2026-11-16")]
    public void The_esv_day_and_the_declaration_day_count_come_from_the_config(
        int quarter,
        int esvDeadlineDay,
        int declarationDays,
        string expectedEsvStatutory,
        string expectedDeclarationStatutory,
        string expectedDeclarationDue)
    {
        var config = ReferenceConfig with
        {
            EsvDeadlineDay = esvDeadlineDay,
            DeclarationDays = declarationDays,
        };

        var actual = DeadlineCalendar.ForQuarter(2026, quarter, config, ReferenceSettings);

        Assert.Equal(Date(expectedEsvStatutory), actual.Esv.Statutory);
        Assert.Equal(Date(expectedDeclarationStatutory), actual.Declaration.Statutory);
        Assert.Equal(Date(expectedDeclarationDue), actual.Declaration.Due);
    }

    [Theory]
    [InlineData(1, "2026-04-19", "2026-05-10")]
    [InlineData(2, "2026-07-19", "2026-08-09")]
    public void A_sunday_deadline_stays_put_when_the_weekend_is_friday_and_saturday(
        int quarter,
        string expectedEsvDue,
        string expectedDeclarationDue)
    {
        var settings = ReferenceSettings with
        {
            WeekendDays = [DayOfWeek.Friday, DayOfWeek.Saturday],
        };

        var actual = DeadlineCalendar.ForQuarter(2026, quarter, ReferenceConfig, settings);

        Assert.Equal(Date(expectedEsvDue), actual.Esv.Due);
        Assert.Equal(Date(expectedDeclarationDue), actual.Declaration.Due);
    }

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
