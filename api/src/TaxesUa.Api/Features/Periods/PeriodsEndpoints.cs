using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Periods;

public static class PeriodsEndpoints
{
    public static IEndpointRouteBuilder MapPeriodsApi(this IEndpointRouteBuilder routes)
    {
        var periods = routes.MapGroup("/periods").WithTags("Periods").RequireAuthorization();

        periods.MapGet("/{year:int}", async (
                int year,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var loaded = await YearAccruals.LoadAsync(database, user.Id, year, cancellationToken);
                if (loaded is null)
                {
                    return Missing(year);
                }

                // Outside the ledger (no registration date, a year before it, or past a missing year)
                // no obligation and no balance is sent rather than a wrong one.
                var ledger = loaded.ViewedIsInLedger
                    ? await loaded.PaymentLedgerAsync(database, user.Id, time.TodayInKyiv(), cancellationToken)
                    : null;

                return Results.Ok(ToResponse(loaded, ledger));
            })
            .Produces<PeriodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static PeriodsResponse ToResponse(LoadedYears loadedYears, PaymentLedger? ledger)
    {
        var loaded = loadedYears.Viewed;
        var year = loaded.Accrual.Year;
        var configInput = loaded.Config.ToEngineInput();
        var settingsInput = loaded.Settings.ToEngineInput();
        var registrationDate = settingsInput.FopRegistrationDate;
        IReadOnlyList<Obligation> obligations = ledger is null
            ? []
            : [.. ledger.SingleTax.Obligations, .. ledger.MilitaryLevy.Obligations, .. ledger.Esv.Obligations];

        var quarters = loaded.Accrual.Quarters
            .Where(accrual => registrationDate is not { } registered
                || QuarterEnd(year, accrual.Income.Quarter) >= registered)
            .Select(accrual => new QuarterPeriodResponse(
                accrual.Income.Quarter,
                accrual.Income.IncomeKop,
                accrual.SingleTaxKop,
                accrual.MilitaryLevyKop,
                accrual.EsvKop,
                accrual.TotalKop,
                accrual.Income.CumulativeIncomeKop,
                accrual.CumulativeSingleTaxKop,
                accrual.CumulativeExcessIncomeKop,
                accrual.CumulativeExcessTaxKop,
                accrual.CumulativeMilitaryLevyKop,
                DeadlineCalendar.ForQuarter(year, accrual.Income.Quarter, configInput, settingsInput),
                ToObligations(obligations, year, accrual.Income.Quarter),
                accrual.Group3))
            .ToArray();

        var months = ledger is null
            ? null
            : loadedYears.AdvancesOf(ledger)?
                .Where(advance => advance.Year == year
                    && (registrationDate is not { } registered || MonthEnd(year, advance.Month) >= registered))
                .Select(advance => new MonthPeriodResponse(
                    advance.Month,
                    advance.IncomeKop,
                    advance.SingleTax.AccruedKop,
                    advance.MilitaryLevy.AccruedKop,
                    advance.Esv.AccruedKop,
                    advance.RecommendedKop,
                    advance.RecommendedDate))
                .ToArray();

        return new PeriodsResponse(
            year,
            ToWarnings(loadedYears),
            LimitCrossingResponse.Of(loaded),
            quarters,
            months,
            ledger is null
                ? null
                : new YearBalancesResponse(
                    ToYearBalance(ledger.SingleTax.ForYear(year)),
                    ToYearBalance(ledger.MilitaryLevy.ForYear(year)),
                    ToYearBalance(ledger.Esv.ForYear(year)),
                    [
                        .. new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }
                            .SelectMany(kind => kind.OutsideGroup3)
                            .Where(payment => payment.PeriodYear == year)
                            .Select(payment => new OutsideGroup3PaymentResponse(
                                payment.Kind,
                                payment.Period.Quarter,
                                (payment.Period as PaymentPeriod.Monthly)?.Month,
                                payment.AmountKop)),
                    ]),
            [.. Enumerable.Range(1, 4).Where(loaded.Accrual.InGroup3)],
            [.. Enumerable.Range(1, 4).Where(quarter => loaded.Accrual.Accrues(PaymentKind.Esv, quarter))]);
    }

    private static QuarterObligations? ToObligations(
        IReadOnlyList<Obligation> obligations, int year, int quarter)
    {
        var ofQuarter = obligations
            .Where(obligation => obligation.Year == year && obligation.Quarter == quarter)
            .ToArray();
        return ofQuarter.Length == 0
            ? null
            : new QuarterObligations(
                ToObligation(ofQuarter.Single(obligation => obligation.Kind == PaymentKind.SingleTax)),
                ToObligation(ofQuarter.Single(obligation => obligation.Kind == PaymentKind.MilitaryLevy)),
                ToObligation(ofQuarter.Single(obligation => obligation.Kind == PaymentKind.Esv)));
    }

    private static ObligationResponse ToObligation(Obligation obligation) => new(
        obligation.AccruedKop,
        obligation.PaidKop,
        obligation.RemainingKop,
        obligation.DueDate,
        obligation.Status);

    private static KindYearBalance ToYearBalance(TaxesUa.Engine.KindYearBalance balance) => new(
        balance.EarlierOwedKop,
        balance.AccruedKop,
        balance.PaidKop,
        balance.OwedKop,
        balance.CreditKop);

    // Folds the engine's per-operation list into one flag or count per kind: the transactions screen
    // already marks each excluded row, so this screen only has to say that some exist.
    private static PeriodWarnings ToWarnings(LoadedYears loadedYears)
    {
        var loaded = loadedYears.Viewed;
        var fopRegistrationDateNotSet = false;
        var excludedOperationCount = 0;
        var negativeQuarters = new List<int>();
        foreach (var warning in loaded.Accrual.Warnings)
        {
            switch (warning)
            {
                case EngineWarning.FopRegistrationDateNotSet:
                    fopRegistrationDateNotSet = true;
                    break;
                case EngineWarning.OperationBeforeRegistration:
                case EngineWarning.RefundOfReceiptBeforeRegistration:
                    excludedOperationCount++;
                    break;
                case EngineWarning.NegativeCumulativeTax negative:
                    negativeQuarters.Add(negative.Quarter);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(loaded), warning, "Unmapped engine warning.");
            }
        }

        return new PeriodWarnings(
            loaded.Config.VerifiedAt is null,
            fopRegistrationDateNotSet,
            excludedOperationCount,
            [.. negativeQuarters],
            loaded.Settings.FopRegistrationDate is { } registered && loaded.Accrual.Year < registered.Year,
            loadedYears.MissingTaxYear,
            BeforeGroup3Response.Of(loaded.Accrual.Income.BeforeGroup3));
    }

    private static DateOnly QuarterEnd(int year, int quarter) => MonthEnd(year, 3 * quarter);

    private static DateOnly MonthEnd(int year, int month) =>
        new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);

    private static IResult Missing(int year) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No tax year configuration exists for {year}.");
}

/// <summary>
/// <c>Months</c> is sent only in <c>MonthlyAdvance</c> mode and only for a year the ledger covers;
/// the mode changes nothing else in this response (Rule 6). <c>Quarters</c> and <c>Months</c> stop at
/// the quarter named by <c>LimitCrossing</c>, when the year's income went over its limit (Rule 4).
/// <c>Group3Quarters</c> are the year's quarters in group 3, before registration included, so a client
/// can tell which single tax and levy periods the ledger leaves out without redoing the crossings.
/// <c>EsvQuarters</c> are those the ledger counts ESV for: the group 3 quarters and the ones before group
/// 3 starts, since ESV does not depend on the tax system.
/// </summary>
internal sealed record PeriodsResponse(
    int Year,
    PeriodWarnings Warnings,
    LimitCrossingResponse? LimitCrossing,
    QuarterPeriodResponse[] Quarters,
    MonthPeriodResponse[]? Months,
    YearBalancesResponse? Balances,
    int[] Group3Quarters,
    int[] EsvQuarters);

/// <summary>
/// One month's accruals and Rule 6's advance for it. <c>RecommendedKop</c> is what of the month's
/// three accruals the oldest-first allocation left unpaid, to pay by <c>RecommendedDate</c>; zero once
/// paid.
/// </summary>
internal sealed record MonthPeriodResponse(
    int Month,
    long IncomeKop,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop,
    long RecommendedKop,
    DateOnly RecommendedDate);

/// <summary>
/// Rule 7's three ledgers for the year, one named field per kind like the engine's
/// <see cref="PaymentLedger"/>, so the web has no collection to pool into one figure either.
/// <c>OutsideGroup3Payments</c> are the payments that name a quarter of this year outside group 3
/// (Rule 4), listed apart because none of the three ledgers counts them.
/// </summary>
internal sealed record YearBalancesResponse(
    KindYearBalance SingleTax,
    KindYearBalance MilitaryLevy,
    KindYearBalance Esv,
    OutsideGroup3PaymentResponse[] OutsideGroup3Payments);

/// <summary>A payment naming a quarter outside group 3, or a month of one.</summary>
internal sealed record OutsideGroup3PaymentResponse(PaymentKind Kind, int Quarter, int? Month, long AmountKop);

/// <summary>
/// One kind's year as the oldest-first allocation left it (Rule 7). <c>EarlierOwedKop</c> is what
/// earlier years still owe, <c>PaidKop</c> what was allocated to this year's quarters, <c>OwedKop</c>
/// what every quarter up to and including this year still owes, and <c>CreditKop</c> the kind's
/// unspent overpayment, nonzero only when nothing is owed anywhere.
/// </summary>
internal sealed record KindYearBalance(
    long EarlierOwedKop, long AccruedKop, long PaidKop, long OwedKop, long CreditKop);

internal sealed record QuarterObligations(
    ObligationResponse SingleTax,
    ObligationResponse MilitaryLevy,
    ObligationResponse Esv);

/// <summary>
/// One kind's obligation for one quarter, as <see cref="Balances"/> allocated it.
/// </summary>
internal sealed record ObligationResponse(
    long AccruedKop,
    long PaidKop,
    long RemainingKop,
    DateOnly DueDate,
    ObligationStatus Status);

/// <summary>
/// Rule 9, Rule 8 and Rule 7 as the screen needs them. Each field is one sentence the interface
/// writes; the api sends no text, per ADR-002. <c>YearBeforeRegistration</c> and
/// <c>MissingTaxYear</c> say why a year with a registration date still has no balances: the year
/// precedes the Rule 7 ledger, or the ledger stopped at that unconfigured year. <c>BeforeGroup3</c>
/// is the part of the year on the general system, whose quarters owe ESV only (<c>Group3</c> false).
/// </summary>
internal sealed record PeriodWarnings(
    bool TaxYearUnverified,
    bool FopRegistrationDateNotSet,
    int ExcludedOperationCount,
    int[] NegativeCumulativeTaxQuarters,
    bool YearBeforeRegistration,
    int? MissingTaxYear,
    BeforeGroup3Response? BeforeGroup3);

/// <summary>
/// The days from registration to the day before group 3 starts that fall in the year, and the net
/// income of that stretch, which the general system taxes and the app does not (Tax Code 298.1.4).
/// </summary>
internal sealed record BeforeGroup3Response(DateOnly From, DateOnly To, long IncomeKop)
{
    public static BeforeGroup3Response? Of(BeforeGroup3? stretch) =>
        stretch is null ? null : new BeforeGroup3Response(stretch.From, stretch.To, stretch.IncomeKop);
}

/// <summary>
/// Rule 4: the income went over the limit in <c>Quarter</c> of <c>Year</c>, so group 3 ends with it and
/// nothing is computed from <c>SwitchFromQuarter</c> of <c>SwitchFromYear</c> on, where the FOP must be on
/// the general system or another group, until <c>BackOnGroup3From</c> when the owner set one after the
/// crossing. Sent for the crossing's year and for every later year it keeps a quarter of out of group 3.
/// </summary>
internal sealed record LimitCrossingResponse(
    int Year, int Quarter, int SwitchFromYear, int SwitchFromQuarter, YearQuarter? BackOnGroup3From)
{
    public static LimitCrossingResponse? Of(YearAccruals year) => year.Accrual.LimitCrossing is not { } crossing
        ? null
        : new LimitCrossingResponse(
            crossing.Year,
            crossing.Quarter,
            crossing.SwitchFromYear,
            crossing.SwitchFromQuarter,
            year.Settings.BackOnGroup3From is { } back && back > crossing.At ? back : null);
}

/// <summary>
/// One quarter's own accruals and the year-to-date figures through it. The cumulative figures are the
/// declaration's numbers: Q1 is the quarter, Q2 the half-year, Q3 nine months, Q4 the year. The single
/// tax includes the excess tax, and the two excess figures are the income over the limit and its tax,
/// nonzero only in the quarter the limit is crossed in (Rule 4). <c>Group3</c> false marks a quarter
/// before group 3 starts, which owes ESV only.
/// </summary>
internal sealed record QuarterPeriodResponse(
    int Quarter,
    long IncomeKop,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop,
    long TotalKop,
    long CumulativeIncomeKop,
    long CumulativeSingleTaxKop,
    long CumulativeExcessIncomeKop,
    long CumulativeExcessTaxKop,
    long CumulativeMilitaryLevyKop,
    QuarterDeadlines Deadlines,
    QuarterObligations? Obligations,
    bool Group3);
