namespace TaxesUa.Engine;

/// <summary>
/// What one kind owes now, as the home screen shows it: the sum of what its open obligations from
/// quarter <c>FromYear/FromQuarter</c> through <c>ToYear/ToQuarter</c> still owe after allocation.
/// <c>DueDate</c> and <c>Status</c> are those of the oldest of them. With <c>AdvanceMonth</c> set the
/// amount is instead Rule 6's recommended advance: what the quarter's months through that one still
/// owe, dated the advance's recommended date and never overdue, since an advance is not an obligation.
/// </summary>
public sealed record KindDebt(
    PaymentKind Kind,
    int FromYear,
    int FromQuarter,
    int ToYear,
    int ToQuarter,
    long AmountKop,
    DateOnly DueDate,
    ObligationStatus Status,
    int? AdvanceMonth);

/// <summary>
/// The single nearest unfinished step, per Rule 5 and Rule 7 of <c>knowledge/business-rules.md</c>.
/// </summary>
public abstract record NextStep
{
    private NextStep()
    {
    }

    /// <summary>Rule 8: without a registration date nothing accrues, so no figure would be true.</summary>
    public sealed record RegistrationDateNotSet : NextStep;

    public sealed record BeforeRegistration(DateOnly RegistrationDate) : NextStep;

    public sealed record AllDone : NextStep;

    /// <summary>
    /// <c>Now</c> is every kind with something already due or overdue, oldest first, or when there is
    /// none, the kinds sharing the nearest upcoming date. The single tax and the military levy share a
    /// deadline, so they come as one step with two amounts; kinds are never added together (Rule 7).
    /// <c>Later</c> is every other kind's debt, earliest first.
    /// </summary>
    public sealed record Pay(IReadOnlyList<KindDebt> Now, IReadOnlyList<KindDebt> Later) : NextStep;

    /// <param name="ledger">Null only when there is nothing to allocate over: without a registration
    /// date, or while today is before the registration year.</param>
    /// <param name="advances">The ledger years' monthly advances in <c>MonthlyAdvance</c> mode, null in
    /// <c>Quarterly</c> mode. They only ever move a step earlier; they never change its debt.</param>
    public static NextStep Find(
        PaymentLedger? ledger,
        DateOnly? registrationDate,
        DateOnly today,
        IReadOnlyList<MonthlyAdvance>? advances)
    {
        if (registrationDate is not { } registered)
        {
            return new RegistrationDateNotSet();
        }

        if (today < registered)
        {
            return new BeforeRegistration(registered);
        }

        ArgumentNullException.ThrowIfNull(ledger);

        var debts = new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }
            .Select(kind => DebtOf(kind, advances, today))
            .OfType<KindDebt>()
            .OrderBy(debt => debt.DueDate)
            .ThenBy(debt => debt.Kind)
            .ToArray();

        if (debts.Length == 0)
        {
            return new AllDone();
        }

        var nearest = debts[0].DueDate;
        var isNow = debts[0].Status == ObligationStatus.Upcoming
            ? (Func<KindDebt, bool>)(debt => debt.DueDate == nearest)
            : debt => debt.Status != ObligationStatus.Upcoming;
        return new Pay([.. debts.Where(isNow)], [.. debts.Where(debt => !isNow(debt))]);
    }

    /// <summary>
    /// What has fallen due is owed together, across years, since allocation settles the oldest first.
    /// A quarter not yet due is only the next step when nothing has fallen due, and then only the
    /// nearest one: the ESV of later quarters accrues up front and is not owed by that date.
    /// </summary>
    private static KindDebt? DebtOf(KindLedger ledger, IReadOnlyList<MonthlyAdvance>? advances, DateOnly today)
    {
        var open = ledger.Obligations.Where(obligation => obligation.RemainingKop > 0).ToArray();
        var fallenDue = open.Where(obligation => obligation.DueDate <= today).ToArray();
        Obligation[] owed = fallenDue.Length > 0 ? fallenDue : [.. open.Take(1)];
        if (owed.Length == 0)
        {
            return null;
        }

        if (fallenDue.Length == 0 && advances is not null && AdvanceOf(owed[0], advances, today) is { } advance)
        {
            return advance;
        }

        return new KindDebt(
            ledger.Kind,
            owed[0].Year,
            owed[0].Quarter,
            owed[^1].Year,
            owed[^1].Quarter,
            owed.Sum(obligation => obligation.RemainingKop),
            owed[0].DueDate,
            owed[0].Status,
            AdvanceMonth: null);
    }

    /// <summary>
    /// The first advance of the quarter still ahead of today that has anything left to pay, carrying
    /// the unpaid remainder of the months before it: a missed advance is caught up, not dropped. Past
    /// the quarter's last advance, or when that date is not before the quarter's own deadline, the
    /// quarterly step stands.
    /// </summary>
    private static KindDebt? AdvanceOf(Obligation obligation, IReadOnlyList<MonthlyAdvance> advances, DateOnly today)
    {
        var remainingKop = 0L;
        foreach (var month in advances
                     .Where(advance => advance.Year == obligation.Year && advance.Quarter == obligation.Quarter)
                     .OrderBy(advance => advance.Month))
        {
            remainingKop += month.Of(obligation.Kind).RemainingKop;
            if (month.RecommendedDate < today || remainingKop == 0)
            {
                continue;
            }

            return month.RecommendedDate < obligation.DueDate
                ? new KindDebt(
                    obligation.Kind,
                    obligation.Year,
                    obligation.Quarter,
                    obligation.Year,
                    obligation.Quarter,
                    remainingKop,
                    month.RecommendedDate,
                    month.RecommendedDate == today ? ObligationStatus.Due : ObligationStatus.Upcoming,
                    month.Month)
                : null;
        }

        return null;
    }
}
