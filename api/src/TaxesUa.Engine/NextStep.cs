namespace TaxesUa.Engine;

/// <summary>
/// What one kind owes now, as the home screen shows it: the sum of what its open obligations from
/// quarter <c>FromYear/FromQuarter</c> through <c>ToYear/ToQuarter</c> still owe after allocation.
/// <c>DueDate</c> and <c>Status</c> are those of the oldest of them.
/// </summary>
public sealed record KindDebt(
    PaymentKind Kind,
    int FromYear,
    int FromQuarter,
    int ToYear,
    int ToQuarter,
    long AmountKop,
    DateOnly DueDate,
    ObligationStatus Status);

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
    public static NextStep Find(PaymentLedger? ledger, DateOnly? registrationDate, DateOnly today)
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

        var debts = new[] { DebtOf(ledger.SingleTax, today), DebtOf(ledger.MilitaryLevy, today), DebtOf(ledger.Esv, today) }
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
    private static KindDebt? DebtOf(KindLedger ledger, DateOnly today)
    {
        var open = ledger.Obligations.Where(obligation => obligation.RemainingKop > 0).ToArray();
        var fallenDue = open.Where(obligation => obligation.DueDate <= today).ToArray();
        Obligation[] owed = fallenDue.Length > 0 ? fallenDue : [.. open.Take(1)];
        if (owed.Length == 0)
        {
            return null;
        }

        return new KindDebt(
            ledger.Kind,
            owed[0].Year,
            owed[0].Quarter,
            owed[^1].Year,
            owed[^1].Quarter,
            owed.Sum(obligation => obligation.RemainingKop),
            owed[0].DueDate,
            owed[0].Status);
    }
}
