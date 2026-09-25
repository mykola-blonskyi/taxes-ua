namespace TaxesUa.Engine;

/// <summary>
/// The tax year's deadline parameters. The year itself is a <c>ForQuarter</c> argument, so it is
/// not repeated here. <c>DeclarationDays</c> and <c>TaxPaymentDaysAfterDeclaration</c> count
/// calendar days. <c>Holidays</c> is empty during martial law, when holidays are business days.
/// </summary>
public sealed record TaxYearConfigInput(
    int EsvDeadlineDay,
    int DeclarationDays,
    int TaxPaymentDaysAfterDeclaration,
    IReadOnlyList<DateOnly> Holidays);

/// <summary>
/// The FOP settings that move a deadline. Both shifting flags are unconfirmed readings of Rule 5,
/// so both values of each stay reachable.
/// </summary>
public sealed record FopSettingsInput(
    IReadOnlyList<DayOfWeek> WeekendDays,
    bool TaxPaymentCountsFromStatutoryDeclarationDate,
    bool ShiftTaxPaymentFromWeekend);

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
        FopSettingsInput settings)
    {
        var scanLimit = statutory.AddYears(1);
        var date = statutory;
        while (!IsBusinessDay(date, config, settings))
        {
            date = date.AddDays(1);
            if (date > scanLimit)
            {
                throw new ArgumentException(
                    $"The configured weekend days and holidays leave no business day in the year "
                    + $"after {statutory:yyyy-MM-dd}.");
            }
        }

        return date;
    }

    private static bool IsBusinessDay(
        DateOnly date,
        TaxYearConfigInput config,
        FopSettingsInput settings) =>
        !settings.WeekendDays.Contains(date.DayOfWeek) && !config.Holidays.Contains(date);
}
