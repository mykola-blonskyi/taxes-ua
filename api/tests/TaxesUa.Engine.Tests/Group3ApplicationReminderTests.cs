using System.Globalization;

namespace TaxesUa.Engine.Tests;

/// <summary>
/// Tax Code 298.1.2: registered on Monday 2026-09-28, the group 3 application is due within ten days,
/// by 2026-10-08, and is reminded until the owner confirms its receipt.
/// </summary>
public class Group3ApplicationReminderTests
{
    private static readonly DateOnly Registered = Date("2026-09-28");

    private static readonly DateOnly Deadline = Date("2026-10-08");

    [Theory]
    [InlineData("2026-09-30 09:00", null)]
    [InlineData("2026-10-01 08:59", null)]
    [InlineData("2026-10-01 09:00", ReminderOffset.WeekBefore)]
    [InlineData("2026-10-07 09:00", ReminderOffset.DayBefore)]
    [InlineData("2026-10-08 09:00", ReminderOffset.OnTheDay)]
    [InlineData("2026-10-09 09:00", null)]
    public void The_application_is_reminded_a_week_before_a_day_before_and_on_the_day(string now, ReminderOffset? offset)
    {
        var due = Due(now, Settings());

        if (offset is null)
        {
            Assert.Empty(due);
            return;
        }

        var reminder = Assert.Single(due);
        Assert.Equal((Deadline, offset.Value), (reminder.Date, reminder.Offset));
        Assert.Equal([new ReminderItem.Group3Application(Registered, Deadline)], reminder.Items);
        Assert.Equal(ReminderKinds.Group3Application, reminder.Kinds);
    }

    public static TheoryData<string, FopSettingsInput> NotPending => new()
    {
        { "confirmed", Settings() with { Group3Confirmed = true } },
        { "group 3 from a later quarter", Settings() with { Group3Since = Date("2027-01-01") } },
        { "no registration date", Settings() with { FopRegistrationDate = null } },
    };

    [Theory]
    [MemberData(nameof(NotPending))]
    public void Nothing_is_reminded_once_the_application_is_not_the_owners_to_file(string label, FopSettingsInput settings)
    {
        Assert.True(Due("2026-10-08 09:00", settings).Count == 0, label);
        Assert.Null(Group3Application.Pending(settings, Config));
    }

    [Fact]
    public void Group3_since_the_registration_date_is_still_pending()
    {
        var settings = Settings() with { Group3Since = Registered };

        Assert.Equal(Deadline, Group3Application.Pending(settings, Config));
        Assert.Equal(Deadline, Group3Application.Deadline(Registered, Config));
    }

    [Fact]
    public void Without_the_registration_years_parameters_there_is_no_reminder()
    {
        var moment = DateTime.ParseExact("2026-10-08 09:00", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(moment);
        var settings = Settings();
        var year = new LedgerYear(Accruals.ForYear(2027, [], Config, settings), Config);

        var due = ReminderPlan.Due(
            [year], settings, Balances.ForYears([year], settings, [], today), null, new HashSet<YearQuarter>(), today, TimeOnly.FromDateTime(moment));

        Assert.Empty(due);
    }

    private static IReadOnlyList<Reminder> Due(string now, FopSettingsInput settings)
    {
        var moment = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(moment);
        var year = new LedgerYear(Accruals.ForYear(2026, [], Config, settings), Config);

        return ReminderPlan.Due(
            [year],
            settings,
            Balances.ForYears([year], settings, [], today),
            null,
            new HashSet<YearQuarter>(),
            today,
            TimeOnly.FromDateTime(moment));
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

    private static FopSettingsInput Settings() => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: Registered,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
