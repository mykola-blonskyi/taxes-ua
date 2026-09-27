using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/dashboard", async (
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

                var today = time.TodayInKyiv();
                var loaded = await YearAccruals.LoadAsync(database, user.Id, today.Year, cancellationToken);

                // A gap in the configured years stops the ledger (see LoadedYears), so any debt shown
                // would leave out that year's and could be wrong.
                if (loaded is null || loaded.MissingTaxYear is not null)
                {
                    return Results.Ok(new DashboardResponse(
                        today,
                        NextStepResponse.Of(NextStepState.MissingTaxYear) with
                        {
                            MissingTaxYear = loaded?.MissingTaxYear ?? today.Year,
                        },
                        [],
                        null));
                }

                var settings = loaded.Viewed.Settings.ToEngineInput();
                PaymentLedger? ledger = null;
                if (loaded.Ledger is [var first, ..])
                {
                    var payments = await PaymentsEndpoints.LoadEngineInputAsync(
                        database, user.Id, first.Accrual.Year, loaded.Ledger[^1].Accrual.Year, cancellationToken);
                    ledger = Balances.ForYears(
                        [.. loaded.Ledger.Select(each => new LedgerYear(each.Accrual, each.Config.ToEngineInput()))],
                        settings,
                        payments,
                        today);
                }

                var step = NextStep.Find(
                    ledger, settings.FopRegistrationDate, today, ledger is null ? null : loaded.AdvancesOf(ledger));
                var burden = step is NextStep.Pay or NextStep.AllDone
                    ? loaded.Viewed.Accrual.BurdenThrough((today.Month + 2) / 3)
                    : null;
                return Results.Ok(new DashboardResponse(
                    today,
                    ToStep(step, today),
                    ledger is null ? [] : Credits(ledger),
                    burden is null ? null : new TaxBurdenResponse(burden.IncomeKop, burden.TaxKop, burden.RateBp)));
            })
            .WithTags("Dashboard")
            .RequireAuthorization()
            .Produces<DashboardResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static KindCreditResponse[] Credits(PaymentLedger ledger) =>
        [
            .. new[] { ledger.SingleTax, ledger.MilitaryLevy, ledger.Esv }
                .Where(kind => kind.CreditKop > 0)
                .Select(kind => new KindCreditResponse(kind.Kind, kind.CreditKop)),
        ];

    private static NextStepResponse ToStep(NextStep step, DateOnly today) => step switch
    {
        NextStep.RegistrationDateNotSet => NextStepResponse.Of(NextStepState.RegistrationDateNotSet),
        NextStep.BeforeRegistration before =>
            NextStepResponse.Of(NextStepState.BeforeRegistration) with { RegistrationDate = before.RegistrationDate },
        NextStep.AllDone => NextStepResponse.Of(NextStepState.AllDone),
        NextStep.Pay pay => NextStepResponse.Of(NextStepState.Pay) with
        {
            Now = [.. pay.Now.Select(debt => ToDebt(debt, today))],
            Later = [.. pay.Later.Select(debt => ToDebt(debt, today))],
        },
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unmapped next step."),
    };

    private static KindDebtResponse ToDebt(KindDebt debt, DateOnly today) => new(
        debt.Kind,
        debt.FromYear,
        debt.FromQuarter,
        debt.ToYear,
        debt.ToQuarter,
        debt.AmountKop,
        debt.DueDate,
        debt.Status,
        debt.DueDate.DayNumber - today.DayNumber,
        debt.AdvanceMonth);
}

/// <summary>
/// <c>Credits</c> lists each kind with unspent credit, which the ledger only holds once nothing of that
/// kind is owed. <c>Burden</c> is sent only for a year the ledger covers.
/// </summary>
internal sealed record DashboardResponse(
    DateOnly Today,
    NextStepResponse NextStep,
    KindCreditResponse[] Credits,
    TaxBurdenResponse? Burden);

internal enum NextStepState
{
    MissingTaxYear,
    RegistrationDateNotSet,
    BeforeRegistration,
    AllDone,
    Pay,
}

/// <summary>
/// <c>Now</c> and <c>Later</c> are filled only for <c>Pay</c>, <c>RegistrationDate</c> only for
/// <c>BeforeRegistration</c> and <c>MissingTaxYear</c> only for its own state. <c>Now</c> is every
/// debt already due or overdue, or else the kinds sharing the nearest date, each with its own amount;
/// there is deliberately no total (Rule 7).
/// </summary>
internal sealed record NextStepResponse(
    NextStepState State,
    DateOnly? RegistrationDate,
    int? MissingTaxYear,
    KindDebtResponse[] Now,
    KindDebtResponse[] Later)
{
    public static NextStepResponse Of(NextStepState state) => new(state, null, null, [], []);
}

/// <summary>
/// One kind's debt, as <see cref="KindDebt"/>. <c>DaysLeft</c> counts Kyiv days to <c>DueDate</c>:
/// zero on the day itself, negative once overdue. <c>AdvanceMonth</c> is set when the step is Rule 6's
/// monthly advance through that month rather than the quarter's deadline.
/// </summary>
internal sealed record KindDebtResponse(
    PaymentKind Kind,
    int FromYear,
    int FromQuarter,
    int ToYear,
    int ToQuarter,
    long AmountKop,
    DateOnly DueDate,
    ObligationStatus Status,
    int DaysLeft,
    int? AdvanceMonth);

internal sealed record KindCreditResponse(PaymentKind Kind, long CreditKop);

/// <summary>Year to date through the current quarter. <c>RateBp</c> is null without income.</summary>
internal sealed record TaxBurdenResponse(long IncomeKop, long TaxKop, long? RateBp);
