namespace TaxesUa.Engine;

/// <summary>
/// The tax year's parameters, in the order the <c>TaxYearConfig</c> entity lists them. The year
/// itself is a <c>ForQuarter</c> argument, so it is not repeated here, and the entity's
/// <c>EsvMonthlyKop</c> is absent for the same reason: it is <c>MinWageKop</c> at <c>EsvRateBp</c>,
/// and a second copy could contradict the two it comes from. <c>DeclarationDays</c> and
/// <c>TaxPaymentDaysAfterDeclaration</c> count calendar days. <c>Holidays</c> is empty during
/// martial law, when holidays are business days. <c>IncomeLimitKop</c> is Rule 4's annual limit as
/// of Jan 1, not prorated for a partial year. <c>LimitWarnThresholdsPct</c> are ascending warn
/// thresholds strictly below 100; the 100% line itself is not configurable here because it is a
/// tax-system-switch fact, not a UI warning. <c>Group3ApplicationDays</c> is the calendar-day window
/// after registration in which the group 3 application keeps group 3 from the registration date
/// (Tax Code 298.1.2); the registration year's value applies.
/// </summary>
public sealed record TaxYearConfigInput(
    long MinWageKop,
    int SingleTaxRateBp,
    int MilitaryLevyRateBp,
    int EsvRateBp,
    int EsvDeadlineDay,
    int DeclarationDays,
    int TaxPaymentDaysAfterDeclaration,
    IReadOnlyList<DateOnly> Holidays,
    long IncomeLimitKop,
    int ExcessRateBp,
    IReadOnlyList<int> LimitWarnThresholdsPct,
    int Group3ApplicationDays);

/// <summary>
/// The FOP settings the engine reads, mirroring the <c>Settings</c> entity. Both shifting flags are
/// unconfirmed readings of Rule 5, so both values of each stay reachable.
/// <c>EsvRegistrationMonthPolicy</c> is <c>FullMonth</c> in the law's reading of Rule 3; <c>Prorated</c>
/// stays available as a setting that does not match it. <c>FopRegistrationDate</c> is nullable like the
/// entity and has no default, so a caller states the absence of a registration date rather than
/// arriving at it by omission. <c>BackOnGroup3From</c> is the quarter the owner says the FOP is back on
/// group 3 from after a limit crossing (Rule 4); a quarter not after the crossing lifts nothing.
/// <c>Group3Since</c> is the day the owner says the DPS register has group 3 from, null for the
/// registration date itself; a later day is the first day of a quarter (Tax Code 298.1.4), and until
/// it the FOP is on the general system, which this engine does not compute. <c>Group3Confirmed</c>
/// is whether the owner holds the DPS receipt for the group 3 application.
/// </summary>
public sealed record FopSettingsInput(
    IReadOnlyList<DayOfWeek> WeekendDays,
    bool TaxPaymentCountsFromStatutoryDeclarationDate,
    bool ShiftTaxPaymentFromWeekend,
    DateOnly? FopRegistrationDate,
    EsvRegistrationMonthPolicy EsvRegistrationMonthPolicy,
    bool EsvExempt,
    YearQuarter? BackOnGroup3From = null,
    DateOnly? Group3Since = null,
    bool Group3Confirmed = false)
{
    /// <summary>
    /// The first day of group 3: the later of the registration date and <c>Group3Since</c>. Null
    /// without a registration date.
    /// </summary>
    public DateOnly? Group3Start => FopRegistrationDate is { } registered
        ? Group3Since is { } since && since > registered ? since : registered
        : null;
}

/// <summary>
/// One deadline. <c>Due</c> is <c>Statutory</c> moved forward off weekends and holidays, or equal
/// to it when the statutory date is already a business day.
/// </summary>
public sealed record Deadline(DateOnly Statutory, DateOnly Due);

/// <summary>
/// The single tax and the military levy share <c>TaxPayment</c>, so a quarter has three deadlines
/// and not four.
/// </summary>
public sealed record QuarterDeadlines(Deadline Esv, Deadline Declaration, Deadline TaxPayment);

/// <summary>
/// Quarterly deadlines per Rule 5 of <c>knowledge/business-rules.md</c>. Nothing here reads a
/// clock, which is what lets a test assert a future year's dates.
/// </summary>
public static class DeadlineCalendar
{
    public static QuarterDeadlines ForQuarter(
        int year,
        int quarter,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);

        var firstOfMonthAfterQuarter = new DateOnly(year, 3 * quarter, 1).AddMonths(1);
        var quarterEnd = firstOfMonthAfterQuarter.AddDays(-1);

        var esvStatutory = new DateOnly(
            firstOfMonthAfterQuarter.Year, firstOfMonthAfterQuarter.Month, config.EsvDeadlineDay);
        var esvDue = NextBusinessDay(esvStatutory, config, settings);

        var declarationStatutory = quarterEnd.AddDays(config.DeclarationDays);
        var declarationDue = NextBusinessDay(declarationStatutory, config, settings);

        var countFrom = settings.TaxPaymentCountsFromStatutoryDeclarationDate
            ? declarationStatutory
            : declarationDue;
        var taxPaymentStatutory = countFrom.AddDays(config.TaxPaymentDaysAfterDeclaration);
        var taxPaymentDue = settings.ShiftTaxPaymentFromWeekend
            ? NextBusinessDay(taxPaymentStatutory, config, settings)
            : taxPaymentStatutory;

        return new QuarterDeadlines(
            new Deadline(esvStatutory, esvDue),
            new Deadline(declarationStatutory, declarationDue),
            new Deadline(taxPaymentStatutory, taxPaymentDue));
    }

    private static DateOnly NextBusinessDay(
        DateOnly statutory,
        TaxYearConfigInput config,
        FopSettingsInput settings) =>
        NextBusinessDay(statutory, config.Holidays, settings.WeekendDays);

    /// <summary>
    /// <paramref name="date"/> itself when it is a business day, else the first business day after it.
    /// </summary>
    public static DateOnly NextBusinessDay(
        DateOnly date,
        IReadOnlyList<DateOnly> holidays,
        IReadOnlyList<DayOfWeek> weekendDays)
    {
        var scanLimit = date.AddYears(1);
        var day = date;
        while (weekendDays.Contains(day.DayOfWeek) || holidays.Contains(day))
        {
            day = day.AddDays(1);
            if (day > scanLimit)
            {
                throw new ArgumentException(
                    $"The configured weekend days and holidays leave no business day in the year "
                    + $"after {date:yyyy-MM-dd}.");
            }
        }

        return day;
    }
}
