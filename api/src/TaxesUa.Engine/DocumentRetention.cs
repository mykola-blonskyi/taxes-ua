namespace TaxesUa.Engine;

/// <summary>
/// A stretch in which limitation periods, and the retention periods that follow them, do not run
/// (Rule 14's retention, Tax Code XX.10.69.36). <c>Start</c> is the first suspended day and
/// <c>End</c> the last, both inclusive; <c>End</c> is null while the suspension lasts.
/// </summary>
public sealed record LimitationSuspension(DateOnly Start, DateOnly? End);

/// <summary>
/// One group 3 declaration of the year: its quarter, and the day it was filed when the owner marked
/// it filed.
/// </summary>
public sealed record RetainedDeclaration(int Quarter, DateOnly? FiledOn);

/// <summary>
/// The last day a year's documents must be kept.
/// </summary>
public abstract record KeepUntil
{
    private KeepUntil()
    {
    }

    /// <summary>The documents are kept through <c>Date</c>.</summary>
    public sealed record On(DateOnly Date) : KeepUntil;

    /// <summary>
    /// The suspension is still open, so the count is stopped: it resumes the day after the suspension
    /// ends and runs <c>DaysAfterSuspension</c> more days. The date is then never before
    /// <c>NotBefore</c>, the day the count would end without the suspension.
    /// </summary>
    public sealed record WhileSuspended(int DaysAfterSuspension, DateOnly NotBefore) : KeepUntil;
}

/// <summary>
/// Rule 14's retention for a year, per Tax Code art. 44.3: documents are kept at least 1095 days,
/// counted from the day the declaration they went into was filed, or from its deadline when it was
/// not, and longer by the time a suspension stops the count.
/// </summary>
public static class DocumentRetention
{
    /// <summary>Tax Code 44.3.3: other documents, which is what a group 3 FOP keeps.</summary>
    public const int RetentionDays = 1095;

    /// <summary>
    /// The latest of the year's declarations' keep-until dates, or null for a year with no group 3
    /// declaration. A declaration not marked filed counts from its deadline as shifted off a weekend
    /// (Tax Code 49.20), which is the last day the law allows.
    /// </summary>
    public static KeepUntil? ForYear(
        int year,
        IReadOnlyList<RetainedDeclaration> declarations,
        TaxYearConfigInput config,
        FopSettingsInput settings,
        LimitationSuspension? suspension)
    {
        var each = declarations
            .Select(declaration => ForDeclaration(
                declaration.FiledOn
                    ?? DeadlineCalendar.ForQuarter(year, declaration.Quarter, config, settings).Declaration.Due,
                suspension))
            .ToArray();
        if (each.Length == 0)
        {
            return null;
        }

        var open = each.OfType<KeepUntil.WhileSuspended>().ToArray();
        return open.Length == 0
            ? new KeepUntil.On(each.Cast<KeepUntil.On>().Max(until => until.Date))
            : new KeepUntil.WhileSuspended(
                open.Max(until => until.DaysAfterSuspension),
                open.Max(until => until.NotBefore));
    }

    /// <summary>
    /// Day 1 of the count is the day after <paramref name="countsFrom"/>, as Tax Code 102.1 counts its
    /// 1095 days, so the last day kept is day 1095. A suspended day is not counted.
    /// </summary>
    public static KeepUntil ForDeclaration(DateOnly countsFrom, LimitationSuspension? suspension)
    {
        var unsuspended = countsFrom.AddDays(RetentionDays);
        if (suspension is null || unsuspended < suspension.Start
            || suspension.End is { } ended && countsFrom >= ended)
        {
            return new KeepUntil.On(unsuspended);
        }

        var countedBefore = Math.Max(0, suspension.Start.DayNumber - countsFrom.DayNumber - 1);
        var remaining = RetentionDays - countedBefore;
        return suspension.End is { } end
            ? new KeepUntil.On(end.AddDays(remaining))
            : new KeepUntil.WhileSuspended(remaining, unsuspended);
    }
}
