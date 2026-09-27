namespace TaxesUa.Engine;

/// <summary>
/// The three payments into the budget, mirroring <c>BudgetPayment.Kind</c>. An enum and not a record
/// union because nothing about a payment varies by kind: Rule 7 keeps the three ledgers apart, and
/// <see cref="PaymentLedger"/> does that by naming one field per kind rather than by branching on this
/// value.
/// </summary>
public enum PaymentKind
{
    SingleTax,
    MilitaryLevy,
    Esv,
}

/// <summary>
/// The period a payment is recorded against. The <c>BudgetPayment</c> entity spells this as a
/// nullable <c>PeriodQuarter</c> beside a nullable <c>PeriodMonth</c>, which admits a row with
/// neither and a row with both; as cases, neither is representable. The period is what the owner
/// named and is shown back; it does not decide which obligation the payment settles, because Rule 7
/// settles the oldest debt of the kind first.
/// </summary>
public abstract record PaymentPeriod
{
    private PaymentPeriod(int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        Quarter = quarter;
    }

    /// <summary>
    /// The quarter this payment names. Has no <c>init</c> accessor on purpose, so a <c>with</c>
    /// expression cannot rewrite it past the guard above.
    /// </summary>
    public int Quarter { get; }

    public sealed record Quarterly : PaymentPeriod
    {
        public Quarterly(int quarter) : base(quarter)
        {
        }
    }

    public sealed record Monthly : PaymentPeriod
    {
        public Monthly(int month) : base(QuarterOf(month)) => Month = month;

        /// <summary>
        /// The month paid for. Kept beside the quarter it falls in because the monthly advances of
        /// Rule 6 reconcile against the month.
        /// </summary>
        public int Month { get; }
    }

    /// <summary>
    /// A month outside 1 to 12 already maps to a quarter the constructor above rejects, so these two
    /// guards exist for the name they report: a caller that passed a month has to be told the month
    /// is wrong, not the quarter it was turned into.
    /// </summary>
    private static int QuarterOf(int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return (month + 2) / 3;
    }
}

/// <summary>
/// One payment into the budget, mirroring the <c>BudgetPayment</c> entity. Its <c>PaidOn</c>,
/// <c>Note</c> and <c>CreatedAt</c> are absent: Rule 7 settles the kind's oldest outstanding obligation
/// first, whatever the payment names and whenever it left the account. <c>PeriodYear</c> still decides
/// which year's view the payment belongs to.
/// </summary>
public sealed record BudgetPaymentInput
{
    public BudgetPaymentInput(
        PaymentKind kind, long amountKop, int periodYear, PaymentPeriod period)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountKop);
        Kind = kind;
        AmountKop = amountKop;
        PeriodYear = periodYear;
        Period = period;
    }

    public PaymentKind Kind { get; }

    /// <summary>
    /// Has no <c>init</c> accessor on purpose, so a <c>with</c> expression cannot slip past the guard
    /// above and turn a payment into a withdrawal from the budget, which Rule 7 does not model.
    /// </summary>
    public long AmountKop { get; }

    public int PeriodYear { get; }

    public PaymentPeriod Period { get; }
}

/// <summary>
/// Where one obligation stands against one day. <c>Done</c> is decided by what is left to pay rather
/// than by the calendar, so it outranks the three date cases: a settled quarter is done however late
/// the day is. Rule 5 makes a deadline day inclusive, so that day is <c>Due</c> and only the day after
/// it is <c>Overdue</c>.
/// </summary>
public enum ObligationStatus
{
    Upcoming,
    Due,
    Overdue,
    Done,
}

/// <summary>A tax year's accruals with the parameters its deadlines are computed from.</summary>
public sealed record LedgerYear(YearAccrual Accrual, TaxYearConfigInput Config);

/// <summary>
/// What one kind owes for one quarter, and when. <c>AccruedKop</c> is the quarter's own accrual and is
/// negative after a refund; that negative part is credit in the kind's pool, not a negative debt, so
/// <c>PaidKop</c> and <c>RemainingKop</c> only ever split <c>max(AccruedKop, 0)</c>. The
/// <c>Obligation</c> entity of <c>knowledge/domain-model.md</c> also lists <c>Month</c> and
/// <c>CumulativeIncomeKop</c>, for the monthly advances of Rule 6 and for the declaration; neither has
/// a case here, and a field no case fills would be a null to explain at every call site.
/// </summary>
public sealed record Obligation(
    int Year,
    int Quarter,
    PaymentKind Kind,
    long AccruedKop,
    long PaidKop,
    long RemainingKop,
    DateOnly StatutoryDate,
    DateOnly DueDate,
    ObligationStatus Status);

/// <summary>
/// One kind's figures for one year, by the period each payment names. Positive is owed, negative is
/// overpaid. <c>OpeningBalanceKop</c> is everything accrued and paid for earlier years, so a debt or
/// an overpayment carries into the year (Rule 7).
/// </summary>
public sealed record KindYearBalance(
    long OpeningBalanceKop,
    long AccruedKop,
    long PaidKop,
    long BalanceKop);

/// <summary>
/// One kind's ledger across the years in range. <c>Obligations</c> are in the order credit settles
/// them, oldest due date first. <c>CreditKop</c> is what is left of the pool once every obligation is
/// settled, and is nonzero only when nothing remains owed.
/// </summary>
public sealed record KindLedger(
    PaymentKind Kind,
    IReadOnlyList<Obligation> Obligations,
    IReadOnlyList<BudgetPaymentInput> Payments,
    long CreditKop)
{
    /// <summary>Positive is owed, negative is overpaid.</summary>
    public long BalanceKop =>
        Obligations.Sum(obligation => obligation.AccruedKop)
        - Payments.Sum(payment => payment.AmountKop);

    public KindYearBalance ForYear(int year)
    {
        var openingKop =
            Obligations.Where(obligation => obligation.Year < year).Sum(obligation => obligation.AccruedKop)
            - Payments.Where(payment => payment.PeriodYear < year).Sum(payment => payment.AmountKop);
        var accruedKop = Obligations
            .Where(obligation => obligation.Year == year)
            .Sum(obligation => obligation.AccruedKop);
        var paidKop = Payments
            .Where(payment => payment.PeriodYear == year)
            .Sum(payment => payment.AmountKop);
        return new KindYearBalance(openingKop, accruedKop, paidKop, openingKop + accruedKop - paidKop);
    }
}

/// <summary>
/// Rule 7 forbids mixing kinds, so the three ledgers are three named fields and not a collection:
/// there is nothing here to iterate or sum across, so a single pooled figure cannot be formed by
/// accident.
/// </summary>
public sealed record PaymentLedger(KindLedger SingleTax, KindLedger MilitaryLevy, KindLedger Esv);

/// <summary>
/// Accrued against paid per kind, per Rule 7 of <c>knowledge/business-rules.md</c>. Within a kind,
/// every payment and every refund quarter's negative accrual goes into one pool that settles the
/// oldest outstanding obligation first, across years, whatever period a payment names: that is how
/// the tax office credits payments against debt (Tax Code art. 87.9). Nothing reads the cumulative
/// accrual fields: ESV has no cumulative counterpart, and a mix of one kind's delta with another's
/// absolute is the one arithmetic mistake this file could make silently. Nothing reads a clock;
/// <c>today</c> is an argument.
/// </summary>
public static class Balances
{
    /// <param name="years">The years in range, in any order and not necessarily contiguous.</param>
    /// <param name="payments">Every payment the owner has. Those named for a year after the last one
    /// in range belong to a later view and are left out; those named for any earlier year, even one
    /// before registration or absent from <paramref name="years"/>, are credit.</param>
    public static PaymentLedger ForYears(
        IReadOnlyList<LedgerYear> years,
        FopSettingsInput settings,
        IReadOnlyList<BudgetPaymentInput> payments,
        DateOnly today)
    {
        if (years.Count == 0)
        {
            throw new ArgumentException("A ledger needs at least one year.", nameof(years));
        }

        if (years.DistinctBy(year => year.Accrual.Year).Count() != years.Count)
        {
            throw new ArgumentException("A year appears twice, which would count it twice.", nameof(years));
        }

        var horizon = years.Max(year => year.Accrual.Year);
        var pooled = payments.Where(payment => payment.PeriodYear <= horizon).ToArray();
        return new PaymentLedger(
            ForKind(PaymentKind.SingleTax, quarter => quarter.SingleTaxKop, deadlines => deadlines.TaxPayment),
            ForKind(PaymentKind.MilitaryLevy, quarter => quarter.MilitaryLevyKop, deadlines => deadlines.TaxPayment),
            ForKind(PaymentKind.Esv, quarter => quarter.EsvKop, deadlines => deadlines.Esv));

        KindLedger ForKind(
            PaymentKind kind,
            Func<QuarterAccrual, long> accruedKop,
            Func<QuarterDeadlines, Deadline> deadlineOf)
        {
            var ofKind = pooled.Where(payment => payment.Kind == kind).ToArray();
            var creditKop = ofKind.Sum(payment => payment.AmountKop);
            if (settings.FopRegistrationDate is null)
            {
                return new KindLedger(kind, [], ofKind, creditKop);
            }

            var due = years
                .SelectMany(year => year.Accrual.Quarters.Select(quarter => (
                    Year: year.Accrual.Year,
                    Quarter: quarter.Income.Quarter,
                    AccruedKop: accruedKop(quarter),
                    Deadline: deadlineOf(DeadlineCalendar.ForQuarter(
                        year.Accrual.Year, quarter.Income.Quarter, year.Config, settings)))))
                .OrderBy(row => row.Deadline.Due)
                .ThenBy(row => row.Year)
                .ThenBy(row => row.Quarter)
                .ToArray();
            creditKop += due.Sum(row => Math.Max(-row.AccruedKop, 0));

            var obligations = new Obligation[due.Length];
            for (var i = 0; i < due.Length; i++)
            {
                var row = due[i];
                var owedKop = Math.Max(row.AccruedKop, 0);
                var paidKop = Math.Min(owedKop, creditKop);
                creditKop -= paidKop;
                obligations[i] = new Obligation(
                    row.Year,
                    row.Quarter,
                    kind,
                    row.AccruedKop,
                    paidKop,
                    owedKop - paidKop,
                    row.Deadline.Statutory,
                    row.Deadline.Due,
                    StatusOf(owedKop - paidKop, row.Deadline.Due, today));
            }

            return new KindLedger(kind, obligations, ofKind, creditKop);
        }
    }

    /// <summary>
    /// The shifted due date decides lateness, not the statutory one: Rule 5 moves a deadline off a
    /// non-working day, so the statutory date can already be past while the obligation is still on
    /// time.
    /// </summary>
    private static ObligationStatus StatusOf(long remainingKop, DateOnly dueDate, DateOnly today) =>
        remainingKop == 0
            ? ObligationStatus.Done
            : today.CompareTo(dueDate) switch
            {
                > 0 => ObligationStatus.Overdue,
                0 => ObligationStatus.Due,
                _ => ObligationStatus.Upcoming,
            };
}
