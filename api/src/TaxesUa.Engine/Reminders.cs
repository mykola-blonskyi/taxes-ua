namespace TaxesUa.Engine;

/// <summary>
/// What a reminder is about, as flags so a message covering several can be compared with the ones
/// already sent: a kind that was in an earlier message of the same date and offset is not new.
/// </summary>
[Flags]
public enum ReminderKinds
{
    None = 0,
    SingleTax = 1,
    MilitaryLevy = 2,
    Esv = 4,
    Declaration = 8,
}

/// <summary>Days from the date a reminder is about to the day it is sent on, at 09:00 Kyiv.</summary>
public enum ReminderOffset
{
    WeekBefore = -7,
    DayBefore = -1,
    OnTheDay = 0,
    DayAfter = 1,
}

public abstract record ReminderItem
{
    private ReminderItem()
    {
    }

    public abstract ReminderKinds Kind { get; }

    /// <summary>
    /// What one kind still owes by the reminder's date after Rule 7's allocation. With
    /// <c>AdvanceMonth</c> set it is Rule 6's advance through that month of the quarter instead, the
    /// earlier months' unpaid remainder included, as the home screen shows it.
    /// </summary>
    public sealed record Payment(PaymentKind PaymentKind, int Year, int Quarter, int? AdvanceMonth, long AmountKop) : ReminderItem
    {
        public override ReminderKinds Kind => PaymentKind switch
        {
            PaymentKind.SingleTax => ReminderKinds.SingleTax,
            PaymentKind.MilitaryLevy => ReminderKinds.MilitaryLevy,
            PaymentKind.Esv => ReminderKinds.Esv,
            _ => throw new ArgumentOutOfRangeException(nameof(PaymentKind), PaymentKind, "Unknown payment kind."),
        };
    }

    /// <summary>A quarter's declaration not yet marked filed.</summary>
    public sealed record Declaration(int Year, int Quarter) : ReminderItem
    {
        public override ReminderKinds Kind => ReminderKinds.Declaration;
    }
}

/// <summary>Everything due on <c>Date</c>, in one message.</summary>
public sealed record Reminder(DateOnly Date, ReminderOffset Offset, IReadOnlyList<ReminderItem> Items)
{
    public ReminderKinds Kinds => Items.Aggregate(ReminderKinds.None, (kinds, item) => kinds | item.Kind);
}

/// <summary>
/// The reminders due at a moment, per the reminders rule of <c>knowledge/business-rules.md</c>.
/// Computed from the ledger every time rather than scheduled ahead, so a payment recorded between two
/// runs changes the amount or drops the reminder. Nothing reads a clock: the Kyiv day and time are
/// arguments.
/// </summary>
public static class ReminderPlan
{
    public static readonly TimeOnly SendAt = new(9, 0);

    private static readonly ReminderOffset[] LatestFirst =
        [ReminderOffset.DayAfter, ReminderOffset.OnTheDay, ReminderOffset.DayBefore, ReminderOffset.WeekBefore];

    /// <param name="years">The ledger's years, which the ledger was allocated over.</param>
    /// <param name="advances">Rule 6's advances in <c>MonthlyAdvance</c> mode, null in <c>Quarterly</c>
    /// mode.</param>
    /// <param name="filed">The quarters whose declaration the owner marked filed.</param>
    public static IReadOnlyList<Reminder> Due(
        IReadOnlyList<LedgerYear> years,
        FopSettingsInput settings,
        PaymentLedger ledger,
        IReadOnlyList<MonthlyAdvance>? advances,
        IReadOnlySet<YearQuarter> filed,
        DateOnly today,
        TimeOnly now)
    {
        var candidates = Payments(ledger)
            .Concat(Advances(ledger, advances))
            .Concat(Declarations(years, settings, filed));

        var reminders = new List<Reminder>();
        foreach (var date in candidates.GroupBy(candidate => candidate.Date).OrderBy(group => group.Key))
        {
            if (OffsetAt(date.Key, today, now) is not { } offset)
            {
                continue;
            }

            ReminderItem[] items =
            [
                .. date.Where(candidate => offset != ReminderOffset.DayAfter || candidate.OverdueToo)
                    .Select(candidate => candidate.Item)
                    .OrderBy(item => item is ReminderItem.Declaration)
                    .ThenBy(item => item.Kind),
            ];
            if (items.Length > 0)
            {
                reminders.Add(new Reminder(date.Key, offset, items));
            }
        }

        return reminders;
    }

    /// <summary>
    /// Only the latest offset whose moment has passed is due, so a server that was down through the
    /// week-before moment and is back after the day-before one sends one message, not two. The ones
    /// before the date go out late, up to the date itself; the day after goes out on that day only, so
    /// an old debt is flagged once and a first run on a new deployment does not replay months of them.
    /// </summary>
    private static ReminderOffset? OffsetAt(DateOnly date, DateOnly today, TimeOnly now)
    {
        foreach (var offset in LatestFirst)
        {
            var sendOn = date.AddDays((int)offset);
            if (sendOn < today || (sendOn == today && now >= SendAt))
            {
                var stillOn = offset == ReminderOffset.DayAfter ? today == sendOn : today <= date;
                return stillOn ? offset : null;
            }
        }

        return null;
    }

    private static IEnumerable<Candidate> Payments(PaymentLedger ledger) =>
        Kinds(ledger)
            .SelectMany(kind => kind.Obligations)
            .Where(obligation => obligation.RemainingKop > 0)
            .Select(obligation => new Candidate(
                obligation.DueDate,
                new ReminderItem.Payment(obligation.Kind, obligation.Year, obligation.Quarter, null, obligation.RemainingKop),
                OverdueToo: true));

    /// <summary>
    /// An advance whose date is not before its quarter's own deadline adds nothing to the quarterly
    /// reminder, and an advance is a recommendation, so it is never overdue (Rule 6).
    /// </summary>
    private static IEnumerable<Candidate> Advances(PaymentLedger ledger, IReadOnlyList<MonthlyAdvance>? advances)
    {
        if (advances is null)
        {
            yield break;
        }

        foreach (var kind in Kinds(ledger))
        {
            foreach (var obligation in kind.Obligations)
            {
                var remainingKop = 0L;
                foreach (var month in advances
                             .Where(advance => advance.Year == obligation.Year && advance.Quarter == obligation.Quarter)
                             .OrderBy(advance => advance.Month))
                {
                    remainingKop += month.Of(kind.Kind).RemainingKop;
                    if (remainingKop > 0 && month.RecommendedDate < obligation.DueDate)
                    {
                        yield return new Candidate(
                            month.RecommendedDate,
                            new ReminderItem.Payment(kind.Kind, month.Year, month.Quarter, month.Month, remainingKop),
                            OverdueToo: false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Rule 15: a quarter has a declaration when it is in group 3 and ends on or after the registration
    /// date. Its reminders stop once it is marked filed, and at the deadline, since a filed mark the
    /// owner forgot to set would otherwise turn into a daily false alarm.
    /// </summary>
    private static IEnumerable<Candidate> Declarations(
        IReadOnlyList<LedgerYear> years, FopSettingsInput settings, IReadOnlySet<YearQuarter> filed)
    {
        if (settings.FopRegistrationDate is not { } registered)
        {
            yield break;
        }

        foreach (var year in years)
        {
            foreach (var quarter in year.Accrual.Quarters.Select(accrual => accrual.Income.Quarter))
            {
                var quarterEnd = new DateOnly(year.Accrual.Year, 3 * quarter, 1).AddMonths(1).AddDays(-1);
                if (quarterEnd < registered || filed.Contains(new YearQuarter(year.Accrual.Year, quarter)))
                {
                    continue;
                }

                var due = DeadlineCalendar.ForQuarter(year.Accrual.Year, quarter, year.Config, settings).Declaration.Due;
                yield return new Candidate(due, new ReminderItem.Declaration(year.Accrual.Year, quarter), OverdueToo: false);
            }
        }
    }

    private static KindLedger[] Kinds(PaymentLedger ledger) => [ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv];

    private sealed record Candidate(DateOnly Date, ReminderItem Item, bool OverdueToo);
}
