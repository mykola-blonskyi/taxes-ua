using System.Globalization;

namespace TaxesUa.Engine.Tests;

/// <summary>
/// Rule 4 and Rule 7: a payment naming a quarter outside group 3 stays out of the group 3 ledger and is
/// listed on its kind's <see cref="KindLedger.OutsideGroup3"/>.
/// </summary>
public class PaymentsOutsideGroup3Tests
{
    private const long LimitKop = 1_009_104_900;
    private const long EsvPerQuarterKop = 570_702;

    /// <summary>Crossed in Q2 2026, so Q3 is outside group 3 until the owner is back from Q4.</summary>
    private static readonly TransactionInput[] CrossedInQ2Of2026 =
    [
        Income("2026-02-10", 600_000_000),
        Income("2026-05-10", 600_000_000),
        Income("2026-11-10", 2_000_000),
    ];

    private static readonly TransactionInput[] NeverCrossed =
    [
        Income("2026-02-10", 6_000_000),
        Income("2026-05-10", 6_000_000),
        Income("2026-11-10", 2_000_000),
    ];

    private static FopSettingsInput BackFromQ4Of2026 =>
        RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2026, 4) };

    [Fact]
    public void A_stopped_quarters_esv_payment_does_not_clear_the_resumed_quarter()
    {
        var stopped = Paid(PaymentKind.Esv, EsvPerQuarterKop, 2026, new PaymentPeriod.Quarterly(3));

        var esv = Ledger(
                CrossedInQ2Of2026,
                BackFromQ4Of2026,
                Paid(PaymentKind.Esv, EsvPerQuarterKop, 2026, new PaymentPeriod.Quarterly(1)),
                Paid(PaymentKind.Esv, EsvPerQuarterKop, 2026, new PaymentPeriod.Quarterly(2)),
                stopped)
            .Esv;

        Assert.Equal(
            [
                (1, EsvPerQuarterKop, EsvPerQuarterKop, 0L, ObligationStatus.Done),
                (2, EsvPerQuarterKop, EsvPerQuarterKop, 0L, ObligationStatus.Done),
                (4, EsvPerQuarterKop, 0L, EsvPerQuarterKop, ObligationStatus.Overdue),
            ],
            esv.Obligations.Select(o => (o.Quarter, o.AccruedKop, o.PaidKop, o.RemainingKop, o.Status)));
        Assert.Equal(0, esv.CreditKop);
        Assert.Equal([stopped], esv.OutsideGroup3);
        Assert.Equal(
            new KindYearBalance(0, 3 * EsvPerQuarterKop, 2 * EsvPerQuarterKop, EsvPerQuarterKop, 0),
            esv.ForYear(2026));
    }

    public static TheoryData<string, PaymentKind, int, PaymentPeriod, bool> Periods => new()
    {
        { "stopped quarter", PaymentKind.Esv, 2026, new PaymentPeriod.Quarterly(3), true },
        { "month of the stopped quarter", PaymentKind.Esv, 2026, new PaymentPeriod.Monthly(8), true },
        { "single tax of the stopped quarter", PaymentKind.SingleTax, 2026, new PaymentPeriod.Quarterly(3), true },
        { "levy of the stopped quarter", PaymentKind.MilitaryLevy, 2026, new PaymentPeriod.Monthly(9), true },
        { "quarter before the crossing", PaymentKind.Esv, 2026, new PaymentPeriod.Quarterly(1), false },
        { "crossing quarter", PaymentKind.SingleTax, 2026, new PaymentPeriod.Quarterly(2), false },
        { "resumed quarter", PaymentKind.Esv, 2026, new PaymentPeriod.Quarterly(4), false },
        { "month of the resumed quarter", PaymentKind.MilitaryLevy, 2026, new PaymentPeriod.Monthly(10), false },
        { "year before the ledger", PaymentKind.Esv, 2025, new PaymentPeriod.Quarterly(3), false },
    };

    [Theory]
    [MemberData(nameof(Periods))]
    public void A_payment_is_pooled_only_when_it_names_a_group_3_quarter(
        string label, PaymentKind kind, int year, PaymentPeriod period, bool outside)
    {
        var payment = Paid(kind, 100_000, year, period);

        var paid = Ledger(CrossedInQ2Of2026, BackFromQ4Of2026, payment);
        var unpaid = Ledger(CrossedInQ2Of2026, BackFromQ4Of2026);

        foreach (var (ledger, without) in paid.All().Zip(unpaid.All()))
        {
            var ofKind = ledger.Kind == kind;
            Assert.True(
                ledger.OutsideGroup3.SequenceEqual(ofKind && outside ? [payment] : []), label);
            Assert.True(
                ledger.Payments.SequenceEqual(ofKind && !outside ? [payment] : []), label);
            if (!ofKind || outside)
            {
                Assert.Equal(without.Obligations, ledger.Obligations);
                Assert.Equal(without.CreditKop, ledger.CreditKop);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Periods))]
    public void A_year_that_never_left_group_3_pools_every_payment_as_before(
        string label, PaymentKind kind, int year, PaymentPeriod period, bool _)
    {
        var payment = Paid(kind, 100_000, year, period);

        var ledger = Ledger(NeverCrossed, RegisteredIn2025, payment).Of(kind);

        Assert.True(ledger.Payments.SequenceEqual([payment]), label);
        Assert.Empty(ledger.OutsideGroup3);
        Assert.Equal(100_000, ledger.Obligations.Sum(o => o.PaidKop) + ledger.CreditKop);
    }

    [Fact]
    public void A_quarter_kept_out_by_an_earlier_years_crossing_is_outside_group_3_too()
    {
        var years = Accruals.ForYears(
            [
                new AccrualYearInput(2026, [Income("2026-11-10", LimitKop + 100)], Config2026),
                new AccrualYearInput(2027, [Income("2027-05-10", 20_000_000)], Config2026),
            ],
            RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2027, 2) });
        var stopped = Paid(PaymentKind.Esv, EsvPerQuarterKop, 2027, new PaymentPeriod.Monthly(2));
        var resumed = Paid(PaymentKind.Esv, EsvPerQuarterKop, 2027, new PaymentPeriod.Quarterly(2));

        var esv = Balances.ForYears(
                [.. years.Select(year => new LedgerYear(year, Config2026))],
                RegisteredIn2025 with { BackOnGroup3From = new YearQuarter(2027, 2) },
                [stopped, resumed],
                Date("2027-12-31"))
            .Esv;

        Assert.Equal([stopped], esv.OutsideGroup3);
        Assert.Equal([resumed], esv.Payments);
    }

    private static PaymentLedger Ledger(
        TransactionInput[] transactions, FopSettingsInput settings, params BudgetPaymentInput[] payments) =>
        Balances.ForYears(
            [new LedgerYear(Accruals.ForYear(2026, transactions, Config2026, settings), Config2026)],
            settings,
            payments,
            Date("2027-03-01"));

    private static BudgetPaymentInput Paid(PaymentKind kind, long amountKop, int year, PaymentPeriod period) =>
        new(kind, amountKop, year, period);

    private static readonly TaxYearConfigInput Config2026 = new(
        MinWageKop: 864_700,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 100,
        EsvRateBp: 2_200,
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: [],
        IncomeLimitKop: LimitKop,
        ExcessRateBp: 1_500,
        LimitWarnThresholdsPct: [85, 100]);

    private static readonly FopSettingsInput RegisteredIn2025 = new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: Date("2025-01-01"),
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static TransactionInput Income(string date, long amountKop) =>
        new TransactionInput.Income(Date(date), amountKop);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
