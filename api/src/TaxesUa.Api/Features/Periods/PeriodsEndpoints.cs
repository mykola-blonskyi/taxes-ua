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

                var payments = await PaymentsEndpoints.LoadEngineInputAsync(
                    database, user.Id, year, cancellationToken);

                return Results.Ok(ToResponse(year, loaded, payments, time.TodayInKyiv()));
            })
            .Produces<PeriodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static PeriodsResponse ToResponse(
        int year, YearAccruals loaded, IReadOnlyList<BudgetPaymentInput> payments, DateOnly today)
    {
        var configInput = loaded.Config.ToEngineInput();
        var settingsInput = loaded.Settings.ToEngineInput();
        var registrationDate = settingsInput.FopRegistrationDate;
        var balances = Balances.ForYear(loaded.Accrual, payments);
        var obligations = ObligationBuilder.ForYear(balances, configInput, settingsInput, today);

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
                ToObligations(obligations, accrual.Income.Quarter)))
            .ToArray();

        return new PeriodsResponse(
            year,
            ToWarnings(loaded),
            quarters,
            // Without a registration date nothing accrues (Rule 8), so every payment would read as an
            // overpayment. No balance is sent rather than a wrong one.
            registrationDate is null
                ? null
                : new YearBalancesResponse(
                    ToYearBalance(balances.SingleTax),
                    ToYearBalance(balances.MilitaryLevy),
                    ToYearBalance(balances.Esv)));
    }

    // The builder emits nothing without a registration date (Rule 8), so neither does the quarter.
    private static QuarterObligations? ToObligations(IReadOnlyList<Obligation> obligations, int quarter)
    {
        var ofQuarter = obligations.Where(obligation => obligation.Quarter == quarter).ToArray();
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
        obligation.OpeningBalanceKop,
        obligation.BalanceKop,
        obligation.DueDate,
        obligation.Status);

    // Sums one kind's own quarters, never across kinds (Rule 7). The closing balance is the engine's.
    private static KindYearBalance ToYearBalance(KindBalance kind) => new(
        kind.Quarters.Sum(quarter => quarter.AccruedKop),
        kind.Quarters.Sum(quarter => quarter.PaidKop),
        kind.ClosingBalanceKop);

    // Folds the engine's per-operation list into one flag or count per kind: the transactions screen
    // already marks each excluded row, so this screen only has to say that some exist.
    private static PeriodWarnings ToWarnings(YearAccruals loaded)
    {
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
            [.. negativeQuarters]);
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
/// <see cref="YearBalances"/>, so the web has no collection to pool into one figure either.
/// </summary>
internal sealed record YearBalancesResponse(
    KindYearBalance SingleTax,
    KindYearBalance MilitaryLevy,
    KindYearBalance Esv);

/// <summary>Positive <c>BalanceKop</c> is owed, negative is overpaid.</summary>
internal sealed record KindYearBalance(long AccruedKop, long PaidKop, long BalanceKop);

internal sealed record QuarterObligations(
    ObligationResponse SingleTax,
    ObligationResponse MilitaryLevy,
    ObligationResponse Esv);

/// <summary>
/// One kind's obligation for one quarter, as <see cref="ObligationBuilder"/> built it.
/// <c>OpeningBalanceKop</c> is the carry-forward from the same kind's previous quarter.
/// </summary>
internal sealed record ObligationResponse(
    long AccruedKop,
    long PaidKop,
    long OpeningBalanceKop,
    long BalanceKop,
    DateOnly DueDate,
    ObligationStatus Status);

/// <summary>
/// Rule 9 and Rule 8 as the screen needs them. Each field is one sentence the interface writes; the
/// api sends no text, per ADR-002.
/// </summary>
internal sealed record PeriodWarnings(
    bool TaxYearUnverified,
    bool FopRegistrationDateNotSet,
    int ExcludedOperationCount,
    int[] NegativeCumulativeTaxQuarters);

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
