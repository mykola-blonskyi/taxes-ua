using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

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
                var overdueInvoices = await InvoicePayments.CountOverdueAsync(database, user.Id, today, cancellationToken);
                var loaded = await YearAccruals.LoadAsync(database, user.Id, today.Year, cancellationToken);
                var declaration = await DeclarationDueAsync(database, user.Id, today, cancellationToken);
                var newTaxYear = await NewTaxYearCheck.LoadAsync(database, today, cancellationToken);
                ExpiredTreasuryAccount[] expiredAccounts =
                    [.. await ExpiredTreasuryAccounts.LoadAsync(database, user.Id, today, cancellationToken)];

                var sync = await SyncHealthCheck.LoadAsync(database, user.Id, time.GetUtcNow(), cancellationToken) is { } health
                    ? new SyncHealthResponse(health.State, health.LastSyncedAt)
                    : null;

                var group3 = await Group3StatusAsync(
                    database,
                    loaded?.Viewed.Settings ?? await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken),
                    loaded?.Viewed.Accrual.Income.BeforeGroup3,
                    today,
                    cancellationToken);

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
                        null,
                        needsReview,
                        declaration,
                        overdueInvoices,
                        group3,
                        sync,
                        newTaxYear,
                        expiredAccounts));
                }

                var settings = loaded.Viewed.Settings.ToEngineInput();
                var ledger = await loaded.PaymentLedgerAsync(database, user.Id, today, cancellationToken);

                var step = NextStep.Find(
                    ledger, settings.FopRegistrationDate, today, ledger is null ? null : loaded.AdvancesOf(ledger));
                var accrual = loaded.Viewed.Accrual;
                var quarter = (today.Month + 2) / 3;
                var burden = step is NextStep.Pay or NextStep.AllDone && accrual.InGroup3(quarter)
                    ? accrual.BurdenThrough(quarter)
                    : null;

                var reserve = ledger is not null && step is NextStep.Pay or NextStep.AllDone
                    ? TaxReserve.Needed(ledger, loaded.LedgerYears, today)
                    : null;

                // Not tied to the next step, unlike burden: the limit bar should show even before there
                // is any next-step debt. Income outside group 3 is not group 3 income, so the bar stops
                // where the accruals do and its excess tax is the one owed; a year with no quarter in
                // group 3 has no bar.
                var limit = accrual.Quarters.LastOrDefault(each => each.Group3) is { } last
                    ? LimitMonitor.Evaluate(last.Income.CumulativeIncomeKop, loaded.Viewed.Config.ToEngineInput())
                    : null;

                return Results.Ok(new DashboardResponse(
                    today,
                    ToStep(step, today),
                    ledger is null ? [] : Credits(ledger),
                    burden is null ? null : new TaxBurdenResponse(burden.IncomeKop, burden.TaxKop, burden.RateBp),
                    limit is null ? null : ToLimit(limit),
                    LimitCrossingResponse.Of(loaded.Viewed),
                    reserve is null ? null : await ToReserveAsync(database, user.Id, reserve, today, time.GetUtcNow(), cancellationToken),
                    needsReview,
                    declaration,
                    overdueInvoices,
                    group3,
                    sync,
                    newTaxYear,
                    expiredAccounts));
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
        // A quarter outside group 3 (Rule 4) has no group 3 declaration to file.
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (loaded is not { Viewed: var viewed }
            || viewed.Settings.FopRegistrationDate is not { } registered
            || DeclarationsEndpoints.QuarterEnd(year, quarter) < registered
            || !viewed.Accrual.InGroup3(quarter))
        {
            return null;
        }

        var due = DeadlineCalendar.ForQuarter(year, quarter, viewed.Config.ToEngineInput(), viewed.Settings.ToEngineInput())
            .Declaration.Due;
        if (today > due
            || await database.DeclarationFilings.AnyAsync(
                row => row.UserId == userId && row.Year == year && row.Quarter == quarter, cancellationToken))
        {
            return null;
        }

        return new DeclarationDueResponse(year, quarter, due, due.DayNumber - today.DayNumber);
    }

    // The application deadline shows while the reminder plan would still remind of it, through the
    // deadline itself: Group3Application.Pending is the predicate both read.
    private static async Task<Group3StatusResponse> Group3StatusAsync(
        AppDbContext database,
        SettingsEntity settings,
        BeforeGroup3? beforeGroup3,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var fop = settings.ToEngineInput();
        var deadline = await DpsStatusEndpoints.RegistrationYearConfigAsync(database, settings, cancellationToken) is { } config
            && Group3Application.Pending(fop, config) is { } pending
            && today <= pending
                ? pending
                : (DateOnly?)null;

        return new Group3StatusResponse(
            fop.Group3Start,
            fop.Group3Confirmed,
            deadline,
            deadline is { } due ? due.DayNumber - today.DayNumber : null,
            BeforeGroup3Response.Of(beforeGroup3));
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

    // The jar comes from what the last sync stored; the dashboard never asks the bank.
    private static async Task<ReserveResponse> ToReserveAsync(
        AppDbContext database,
        string userId,
        ReserveNeed reserve,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var jar = await database.ReserveJars.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var canChooseJar = jar is null && await database.MonobankConnections.AnyAsync(
            row => row.UserId == userId && row.RejectedAt == null, cancellationToken);
        var cover = jar is null ? null : TaxReserve.Cover(reserve, jar.BalanceKop);

        return new ReserveResponse(
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
            ],
            jar is null || cover is null
                ? null
                : new ReserveJarCoverResponse(
                    jar.Title,
                    jar.BalanceKop,
                    jar.FetchedAt,
                    ReserveJarService.IsStale(jar.FetchedAt, now),
                    cover.SurplusKop,
                    cover.ShortfallKop,
                    cover.TopUpBy,
                    cover.TopUpKop,
                    cover.TopUpBy is { } by ? by.DayNumber - today.DayNumber : null),
            canChooseJar);
    }

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
/// before there is any next-step debt. <c>LimitCrossing</c> is sent once the year's income went over its
/// limit (Rule 4): the quarters after it have no obligations, so the next step and the reserve stop at
/// it, and <c>Burden</c> is not sent while today is past it. <c>Reserve</c> is sent when the
/// registration date is set and reached, with the same rule as <c>Burden</c>. <c>NeedsReviewCount</c> is the number of imported transactions
/// the owner has not reviewed, which the figures already count under their suggested kinds, and of
/// budget payment candidates, which count nowhere until confirmed. <c>Declaration</c> is the last ended
/// quarter's declaration while it is due and not marked filed (Rule 15). <c>OverdueInvoiceCount</c> is the
/// number of issued invoices past their due date in Kyiv that their linked receipts do not cover (Rule 14).
/// <c>Sync</c> is the bank sync's health, sent whenever the owner follows a monobank account (Rule 17).
/// <c>ExpiredTreasuryAccounts</c> are the accounts in use whose end has passed, so the owner has to enter the new
/// one (Rule 16).
/// </summary>
internal sealed record DashboardResponse(
    DateOnly Today,
    NextStepResponse NextStep,
    KindCreditResponse[] Credits,
    TaxBurdenResponse? Burden,
    LimitStatusResponse? Limit,
    LimitCrossingResponse? LimitCrossing,
    ReserveResponse? Reserve,
    int NeedsReviewCount,
    DeclarationDueResponse? Declaration,
    int OverdueInvoiceCount,
    Group3StatusResponse Group3,
    SyncHealthResponse? Sync,
    NewTaxYearStatus? NewTaxYear,
    ExpiredTreasuryAccount[] ExpiredTreasuryAccounts);

/// <summary>
/// <c>LastSyncedAt</c> is the end of the oldest statement window a followed account has caught up to, null
/// while an account is still backfilling.
/// </summary>
internal sealed record SyncHealthResponse(SyncHealthState State, DateTimeOffset? LastSyncedAt);

/// <summary>
/// The FOP's group 3 status with the DPS. <c>Group3Start</c> is the first day the figures count as
/// group 3, null without a registration date. <c>ApplicationDeadline</c> is Tax Code 298.1.2's last
/// day for the application, sent while it is unconfirmed, group 3 is expected from registration and
/// the day has not passed; <c>ApplicationDaysLeft</c> counts Kyiv days to it. <c>BeforeGroup3</c> is
/// this year's stretch on the general system, between registration and <c>Group3Start</c>.
/// </summary>
internal sealed record Group3StatusResponse(
    DateOnly? Group3Start,
    bool Confirmed,
    DateOnly? ApplicationDeadline,
    int? ApplicationDaysLeft,
    BeforeGroup3Response? BeforeGroup3);

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
/// derived figure and not a balance. <c>Jar</c> is the monobank jar the owner keeps for taxes compared with
/// <c>TotalKop</c>, from its last stored balance, and is null without one; <c>CanChooseJar</c> is true when
/// there is no jar yet but a monobank token that could offer them.
/// </summary>
internal sealed record ReserveResponse(
    long TotalKop, ReserveDueResponse[] Dues, ReserveJarCoverResponse? Jar, bool CanChooseJar);

/// <summary>
/// The jar's name and balance, shown to the owner only, and when the balance was true (<c>Stale</c> once it
/// is over a day old). Short: <c>ShortfallKop</c> is the whole gap to <c>TotalKop</c> and <c>TopUpKop</c>
/// what must be there by <c>TopUpBy</c>, the first deadline the balance does not cover (<c>TopUpDaysLeft</c>
/// is negative when it has passed). Covered: <c>SurplusKop</c>, zero when exactly covered.
/// </summary>
internal sealed record ReserveJarCoverResponse(
    string Title,
    long BalanceKop,
    DateTimeOffset FetchedAt,
    bool Stale,
    long SurplusKop,
    long ShortfallKop,
    DateOnly? TopUpBy,
    long TopUpKop,
    int? TopUpDaysLeft);

internal sealed record ReserveDueResponse(
    DateOnly DueDate,
    ObligationStatus Status,
    int DaysLeft,
    long SingleTaxKop,
    long MilitaryLevyKop,
    long EsvKop,
    long TotalKop);
