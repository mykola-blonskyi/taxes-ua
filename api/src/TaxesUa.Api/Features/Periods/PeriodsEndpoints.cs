using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Payments;
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

                IReadOnlyList<BudgetPaymentInput> payments = loaded.Ledger is [var first, ..]
                    ? await PaymentsEndpoints.LoadEngineInputAsync(
                        database, user.Id, first.Accrual.Year, loaded.Ledger[^1].Accrual.Year, cancellationToken)
                    : [];

                return Results.Ok(ToResponse(loaded, payments, time.TodayInKyiv()));
            })
            .Produces<PeriodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static PeriodsResponse ToResponse(
        LoadedYears loadedYears, IReadOnlyList<BudgetPaymentInput> payments, DateOnly today)
    {
        var loaded = loadedYears.Viewed;
        var year = loaded.Accrual.Year;
        var configInput = loaded.Config.ToEngineInput();
        var settingsInput = loaded.Settings.ToEngineInput();
        var registrationDate = settingsInput.FopRegistrationDate;
        // Outside the ledger (no registration date, a year before it, or past a missing year) nothing
        // can be allocated honestly, so no obligation and no balance is sent rather than a wrong one.
        var ledger = loadedYears.ViewedIsInLedger
            ? Balances.ForYears(
                [.. loadedYears.Ledger.Select(each => new LedgerYear(each.Accrual, each.Config.ToEngineInput()))],
                settingsInput,
                payments,
                today)
            : null;
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
                accrual.CumulativeMilitaryLevyKop,
                DeadlineCalendar.ForQuarter(year, accrual.Income.Quarter, configInput, settingsInput),
                ToObligations(obligations, year, accrual.Income.Quarter)))
            .ToArray();

        return new PeriodsResponse(
            year,
            ToWarnings(loadedYears),
            quarters,
            ledger is null
                ? null
                : new YearBalancesResponse(
                    ToYearBalance(ledger.SingleTax.ForYear(year)),
                    ToYearBalance(ledger.MilitaryLevy.ForYear(year)),
                    ToYearBalance(ledger.Esv.ForYear(year))));
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
            loadedYears.MissingTaxYear);
    }

    private static DateOnly QuarterEnd(int year, int quarter) =>
        new DateOnly(year, 3 * quarter, 1).AddMonths(1).AddDays(-1);

    private static IResult Missing(int year) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No tax year configuration exists for {year}.");
}

internal sealed record PeriodsResponse(
    int Year,
    PeriodWarnings Warnings,
    QuarterPeriodResponse[] Quarters,
    YearBalancesResponse? Balances);

/// <summary>
/// Rule 7's three ledgers for the year, one named field per kind like the engine's
/// <see cref="PaymentLedger"/>, so the web has no collection to pool into one figure either.
/// </summary>
internal sealed record YearBalancesResponse(
    KindYearBalance SingleTax,
    KindYearBalance MilitaryLevy,
    KindYearBalance Esv);

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
/// precedes the Rule 7 ledger, or the ledger stopped at that unconfigured year.
/// </summary>
internal sealed record PeriodWarnings(
    bool TaxYearUnverified,
    bool FopRegistrationDateNotSet,
    int ExcludedOperationCount,
    int[] NegativeCumulativeTaxQuarters,
    bool YearBeforeRegistration,
    int? MissingTaxYear);

/// <summary>
/// One quarter's own accruals and the year-to-date figures through it. The cumulative three are the
/// declaration's numbers: Q1 is the quarter, Q2 the half-year, Q3 nine months, Q4 the year.
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
    long CumulativeMilitaryLevyKop,
    QuarterDeadlines Deadlines,
    QuarterObligations? Obligations);
