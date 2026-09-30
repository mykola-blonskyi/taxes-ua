using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
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
                var needsReview = await database.Transactions.CountAsync(
                        row => row.UserId == user.Id && row.ReviewStatus == ReviewStatus.NeedsReview, cancellationToken)
                    + await PaymentCandidatesEndpoints.CountPendingAsync(database, user.Id, cancellationToken);
                var loaded = await YearAccruals.LoadAsync(database, user.Id, today.Year, cancellationToken);
                var declaration = await DeclarationDueAsync(database, user.Id, today, cancellationToken);

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
                        null,
                        null,
                        null,
                        needsReview,
                        declaration));
                }

                var settings = loaded.Viewed.Settings.ToEngineInput();
                var ledger = await loaded.PaymentLedgerAsync(database, user.Id, today, cancellationToken);

                var step = NextStep.Find(
                    ledger, settings.FopRegistrationDate, today, ledger is null ? null : loaded.AdvancesOf(ledger));
                var burden = step is NextStep.Pay or NextStep.AllDone
                    ? loaded.Viewed.Accrual.BurdenThrough((today.Month + 2) / 3)
                    : null;

                var reserve = ledger is not null && step is NextStep.Pay or NextStep.AllDone
                    ? TaxReserve.Needed(ledger, loaded.LedgerYears, today)
                    : null;

                // Unconditional, unlike burden: the limit bar should show even before there is any
                // next-step debt.
                var limit = LimitMonitor.Evaluate(
                    loaded.Viewed.Accrual.Quarters[^1].Income.CumulativeIncomeKop,
                    loaded.Viewed.Config.ToEngineInput());

                return Results.Ok(new DashboardResponse(
                    today,
                    ToStep(step, today),
                    ledger is null ? [] : Credits(ledger),
                    burden is null ? null : new TaxBurdenResponse(burden.IncomeKop, burden.TaxKop, burden.RateBp),
                    ToLimit(limit),
                    reserve is null ? null : ToReserve(reserve, today),
                    needsReview,
                    declaration));
            })
            .WithTags("Dashboard")
            .RequireAuthorization()
            .Produces<DashboardResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    // The most recently ended quarter, so in January to March the year before's Q4. Shown from the day
    // after the quarter ends through the declaration's due date, until the owner marks it filed.
    private static async Task<DeclarationDueResponse?> DeclarationDueAsync(
        AppDbContext database, string userId, DateOnly today, CancellationToken cancellationToken)
    {
        var (year, quarter) = today.Month <= 3 ? (today.Year - 1, 4) : (today.Year, (today.Month - 1) / 3);
        var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken);
        if (settings.FopRegistrationDate is not { } registered || DeclarationsEndpoints.QuarterEnd(year, quarter) < registered)
        {
            return null;
        }

        var config = await database.TaxYearConfigs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Year == year, cancellationToken);
        if (config is null)
        {
            return null;
        }

        var due = DeadlineCalendar.ForQuarter(year, quarter, config.ToEngineInput(), settings.ToEngineInput())
            .Declaration.Due;
        if (today > due
            || await database.DeclarationFilings.AnyAsync(
                row => row.UserId == userId && row.Year == year && row.Quarter == quarter, cancellationToken))
        {
            return null;
        }

        return new DeclarationDueResponse(year, quarter, due, due.DayNumber - today.DayNumber);
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

    private static ReserveResponse ToReserve(ReserveNeed reserve, DateOnly today) => new(
        reserve.TotalKop,
        [
            .. reserve.Dues.Select(due => new ReserveDueResponse(
                due.DueDate,
                due.Status,
                due.DueDate.DayNumber - today.DayNumber,
                due.SingleTaxKop,
                due.MilitaryLevyKop,
                due.EsvKop,
                due.TotalKop)),
        ]);

    private static LimitStatusResponse ToLimit(LimitStatus limit) => new(
        limit.IncomeKop,
        limit.LimitKop,
        limit.PercentBp,
        limit.Level,
        limit.RemainingKop,
        limit.ExcessKop,
        limit.ExcessTaxKop);
}

/// <summary>
/// <c>Credits</c> lists each kind with unspent credit, which the ledger only holds once nothing of that
/// kind is owed. <c>Burden</c> is sent only for a year the ledger covers. <c>Limit</c> is sent
/// whenever a tax year is configured, unlike <c>Burden</c>, since the limit bar should show even
/// before there is any next-step debt. <c>Reserve</c> is sent when the registration date is set and
/// reached, with the same rule as <c>Burden</c>. <c>NeedsReviewCount</c> is the number of imported transactions
/// the owner has not reviewed, which the figures already count under their suggested kinds, and of
/// budget payment candidates, which count nowhere until confirmed. <c>Declaration</c> is the last ended
/// quarter's declaration while it is due and not marked filed (Rule 14).
/// </summary>
internal sealed record DashboardResponse(
    DateOnly Today,
    NextStepResponse NextStep,
    KindCreditResponse[] Credits,
    TaxBurdenResponse? Burden,
    LimitStatusResponse? Limit,
    ReserveResponse? Reserve,
    int NeedsReviewCount,
    DeclarationDueResponse? Declaration);

/// <summary><c>DaysLeft</c> counts Kyiv days to <c>DueDate</c>, zero on the day itself.</summary>
internal sealed record DeclarationDueResponse(int Year, int Quarter, DateOnly DueDate, int DaysLeft);

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

/// <summary>
/// As <see cref="LimitStatus"/>. <c>RemainingKop</c> is 0 once <c>Level</c> is <c>Exceeded</c>, where
/// <c>ExcessKop</c>/<c>ExcessTaxKop</c> apply instead.
/// </summary>
internal sealed record LimitStatusResponse(
    long IncomeKop,
    long LimitKop,
    int PercentBp,
    LimitLevel Level,
    long RemainingKop,
    long ExcessKop,
    long ExcessTaxKop);

/// <summary>
/// What the taxes need by now (Rule 13): every accrued and unpaid amount plus the current quarter to
/// date, grouped by due date, oldest first. <c>TotalKop</c> adds the kinds, which is why it is a
/// derived figure and not a balance.
/// </summary>
internal sealed record ReserveResponse(long TotalKop, ReserveDueResponse[] Dues);

internal sealed record ReserveDueResponse(
    DateOnly DueDate,
    ObligationStatus Status,
    int DaysLeft,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop,
    long TotalKop);
