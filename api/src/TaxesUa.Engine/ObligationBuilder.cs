namespace TaxesUa.Engine;

/// <summary>
/// Where one obligation stands against one day. <c>Done</c> is decided by the balance rather than by
/// the calendar, so it outranks the three date cases: a settled quarter is done however late the day
/// is. Rule 5 makes a deadline day inclusive, so that day is <c>Due</c> and only the day after it is
/// <c>Overdue</c>.
/// </summary>
public enum ObligationStatus
{
    Upcoming,
    Due,
    Overdue,
    Done,
}

/// <summary>
/// What one kind owes for one quarter, and when. The <c>Obligation</c> entity of
/// <c>knowledge/domain-model.md</c> also lists <c>Month</c> and <c>CumulativeIncomeKop</c>, for the
/// monthly advances of Rule 6 and for the declaration; neither has a case here, and a field no case
/// fills would be a null to explain at every call site. A declaration in particular could not reach
/// <c>Done</c>, because nothing in the MVP records that one was filed.
/// </summary>
public sealed record Obligation(
    int Year,
    int Quarter,
    PaymentKind Kind,
    long AccruedKop,
    long PaidKop,
    long OpeningBalanceKop,
    long BalanceKop,
    DateOnly StatutoryDate,
    DateOnly DueDate,
    ObligationStatus Status);

/// <summary>
/// The year's obligations, one per kind per quarter, per Rule 5 and Rule 7 of
/// <c>knowledge/business-rules.md</c>. A status is derived from that obligation's own due date and its
/// own balance and from nothing else, so no ordering between the ESV, declaration and payment
/// deadlines is assumed: a payment deadline that lands before the declaration it is counted from still
/// decides its own lateness. Nothing here reads a clock; <c>today</c> is an argument.
/// </summary>
public static class ObligationBuilder
{
    public static IReadOnlyList<Obligation> ForYear(
        YearBalances balances,
        TaxYearConfigInput config,
        FopSettingsInput settings,
        DateOnly today)
    {
        if (settings.FopRegistrationDate is null)
        {
            return [];
        }

        var obligations = new List<Obligation>(12);
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var deadlines = DeadlineCalendar.ForQuarter(balances.Year, quarter, config, settings);
            obligations.Add(
                Build(balances.Year, quarter, balances.SingleTax, deadlines.TaxPayment, today));
            obligations.Add(
                Build(balances.Year, quarter, balances.MilitaryLevy, deadlines.TaxPayment, today));
            obligations.Add(Build(balances.Year, quarter, balances.Esv, deadlines.Esv, today));
        }

        return obligations;
    }

    private static Obligation Build(
        int year,
        int quarter,
        KindBalance kind,
        Deadline deadline,
        DateOnly today)
    {
        var balance = kind.Quarters[quarter - 1];
        return new Obligation(
            year,
            quarter,
            kind.Kind,
            balance.AccruedKop,
            balance.PaidKop,
            balance.OpeningBalanceKop,
            balance.BalanceKop,
            deadline.Statutory,
            deadline.Due,
            StatusOf(balance.BalanceKop, deadline.Due, today));
    }

    /// <summary>
    /// The shifted due date decides lateness, not the statutory one: Rule 5 moves a deadline off a
    /// non-working day, so the statutory date can already be past while the obligation is still on
    /// time.
    /// </summary>
    private static ObligationStatus StatusOf(long balanceKop, DateOnly dueDate, DateOnly today) =>
        balanceKop <= 0
            ? ObligationStatus.Done
            : today.CompareTo(dueDate) switch
            {
                > 0 => ObligationStatus.Overdue,
                0 => ObligationStatus.Due,
                _ => ObligationStatus.Upcoming,
            };
}
