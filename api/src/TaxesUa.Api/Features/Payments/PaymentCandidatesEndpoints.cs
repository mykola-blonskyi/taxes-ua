using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

public static class PaymentCandidatesEndpoints
{
    public static IEndpointRouteBuilder MapPaymentCandidatesApi(this IEndpointRouteBuilder routes)
    {
        var candidates = routes.MapGroup("/payments/candidates").WithTags("Payments").RequireAuthorization();

        // Every year at once, as the transaction review: a backfill can leave candidates in any year.
        candidates.MapGet("", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var pending = await database.BudgetPaymentCandidates
                    .Where(row => row.UserId == user.Id && row.Status == CandidateStatus.Pending)
                    .OrderByDescending(row => row.BankTime)
                    .ToListAsync(cancellationToken);
                var ibans = pending.Select(row => row.CounterIban).Distinct().ToList();
                var learned = (await database.BudgetPaymentCandidates
                        .Where(row => row.UserId == user.Id
                            && row.Status == CandidateStatus.Confirmed
                            && ibans.Contains(row.CounterIban))
                        .Select(row => new { row.CounterIban, row.ConfirmedKind, row.ResolvedAt })
                        .ToListAsync(cancellationToken))
                    .GroupBy(row => row.CounterIban)
                    .ToDictionary(group => group.Key, group => group.MaxBy(row => row.ResolvedAt)!.ConfirmedKind);
                var paidOn = pending.Select(row => row.PaidOn).Distinct().ToList();
                var manual = await database.BudgetPayments
                    .Where(row => row.UserId == user.Id && row.ExternalId == null && paidOn.Contains(row.PaidOn))
                    .OrderBy(row => row.CreatedAt)
                    .ToListAsync(cancellationToken);

                return Results.Ok(pending
                    .Select(row => new PaymentCandidateResponse(
                        row.Id,
                        row.PaidOn,
                        row.AmountKop,
                        row.CounterName,
                        row.CounterIban,
                        row.Purpose,
                        TreasuryPayment.Suggest(row.CounterIban, row.Purpose, learned.GetValueOrDefault(row.CounterIban)),
                        [.. Matches(row, manual).Select(payment => new PaymentMatchResponse(
                            payment.Id,
                            payment.Kind,
                            payment.PeriodYear,
                            payment.PeriodQuarter,
                            payment.PeriodMonth,
                            payment.Note))]))
                    .ToArray());
            })
            .Produces<PaymentCandidateResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        candidates.MapPost("/{id:guid}/confirm", async (
                Guid id,
                ConfirmCandidateRequest request,
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

                // The sync inserts candidates under the same lock, and a second confirm must see the first.
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await LockOwnerAsync(database, user.Id, cancellationToken);
                var candidate = await database.BudgetPaymentCandidates
                    .FirstOrDefaultAsync(row => row.Id == id && row.UserId == user.Id, cancellationToken);
                if (candidate is null)
                {
                    return Missing(id);
                }

                if (candidate.Status != CandidateStatus.Pending)
                {
                    return Conflict(ProblemCodes.CandidateAlreadyDecided, $"The candidate is already {candidate.Status}. Reload the list.");
                }

                var matches = Matches(
                    candidate,
                    await database.BudgetPayments
                        .Where(row => row.UserId == user.Id
                            && row.ExternalId == null
                            && row.PaidOn == candidate.PaidOn
                            && row.Kind == request.Kind)
                        .ToListAsync(cancellationToken));
                var now = time.GetUtcNow();
                BudgetPayment payment;
                if (request.LinkPaymentId is { } linkId)
                {
                    if (matches.FirstOrDefault(row => row.Id == linkId) is not { } match)
                    {
                        return Conflict(
                            ProblemCodes.CandidateLinkChanged,
                            "The payment to link no longer has this date, kind and amount. Reload the list.");
                    }

                    payment = match;
                }
                else if (matches.Count > 0 && !request.RecordSeparately)
                {
                    // A payment the owner typed since the list was read would otherwise be recorded twice.
                    return Conflict(
                        ProblemCodes.CandidateMatchRecorded,
                        "A payment you recorded has the same date, kind and amount. Reload to link it.");
                }
                else
                {
                    var paymentRequest = new PaymentRequest(
                        candidate.PaidOn,
                        request.Kind,
                        candidate.AmountKop,
                        request.PeriodYear,
                        request.PeriodQuarter,
                        request.PeriodMonth,
                        Note: null);
                    if (PaymentsEndpoints.Validate(paymentRequest) is { } errors)
                    {
                        return Problems.Validation(errors);
                    }

                    payment = new BudgetPayment { Id = Guid.NewGuid(), UserId = user.Id, CreatedAt = now };
                    PaymentsEndpoints.Apply(payment, paymentRequest, now);
                    database.BudgetPayments.Add(payment);
                }

                payment.BankAccountId = candidate.BankAccountId;
                payment.ExternalId = candidate.ExternalId;
                payment.UpdatedAt = now;
                candidate.Status = CandidateStatus.Confirmed;
                candidate.ConfirmedKind = request.Kind;
                candidate.ResolvedAt = now;
                var account = await TreasuryAccountsEndpoints.LearnAsync(database, candidate, request.Kind, now, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                var notice = account.NoticeAt is not null && account.LearnedExternalId == candidate.ExternalId
                    ? new ManualAccountNotice(account.ManualIban!)
                    : null;

                return Results.Ok(new ConfirmCandidateResponse(PaymentsEndpoints.ToResponse(payment, settings), notice));
            })
            .Produces<ConfirmCandidateResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        candidates.MapPost("/{id:guid}/dismiss", async (
                Guid id,
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

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await LockOwnerAsync(database, user.Id, cancellationToken);
                var candidate = await database.BudgetPaymentCandidates
                    .FirstOrDefaultAsync(row => row.Id == id && row.UserId == user.Id, cancellationToken);
                if (candidate is null)
                {
                    return Missing(id);
                }

                if (candidate.Status == CandidateStatus.Confirmed)
                {
                    return Conflict(
                        ProblemCodes.CandidateAlreadyConfirmed,
                        "The candidate is already confirmed. Delete its payment instead.");
                }

                if (candidate.Status == CandidateStatus.Pending)
                {
                    candidate.Status = CandidateStatus.Dismissed;
                    candidate.ResolvedAt = time.GetUtcNow();
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    internal static Task<int> CountPendingAsync(AppDbContext database, string userId, CancellationToken cancellationToken) =>
        database.BudgetPaymentCandidates.CountAsync(
            row => row.UserId == userId && row.Status == CandidateStatus.Pending, cancellationToken);

    /// <summary>Pending candidates whose payment date in Kyiv falls in <paramref name="first"/> to <paramref name="last"/>.</summary>
    internal static Task<int> CountPendingAsync(
        AppDbContext database, string userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var from = first.KyivMidnight();
        var until = last.AddDays(1).KyivMidnight();
        return database.BudgetPaymentCandidates.CountAsync(
            row => row.UserId == userId
                && row.Status == CandidateStatus.Pending
                && row.BankTime >= from
                && row.BankTime < until,
            cancellationToken);
    }

    // A payment the owner typed for the same operation: same Kyiv date and amount, and not yet linked to
    // any bank operation. The kind is the one the owner picks, so every kind is offered.
    private static List<BudgetPayment> Matches(BudgetPaymentCandidate candidate, IEnumerable<BudgetPayment> manual) =>
        [.. manual.Where(payment => payment.ExternalId is null
            && payment.PaidOn == candidate.PaidOn
            && payment.AmountKop == candidate.AmountKop)];

    /// <summary>
    /// Follows a change to a payment that came from a bank operation onto its candidate, in the caller's
    /// transaction: a deleted payment (<paramref name="kind"/> null) makes the operation pending again, and
    /// an edited one makes its new kind the owner's latest word on the account. Either way the confirmation
    /// stops teaching the kind it was, and a new kind learns from it (Rule 12).
    /// </summary>
    internal static async Task FollowPaymentAsync(
        AppDbContext database,
        BudgetPayment payment,
        PaymentKind? kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (payment.BankAccountId is not { } accountId || payment.ExternalId is not { } externalId)
        {
            return;
        }

        var candidate = await database.BudgetPaymentCandidates.FirstOrDefaultAsync(
            row => row.UserId == payment.UserId
                && row.BankAccountId == accountId
                && row.ExternalId == externalId
                && row.Status == CandidateStatus.Confirmed,
            cancellationToken);
        if (candidate is null)
        {
            return;
        }

        var previous = candidate.ConfirmedKind!.Value;
        if (kind is { } newKind)
        {
            candidate.ConfirmedKind = newKind;
            candidate.ResolvedAt = now;
            if (newKind == previous)
            {
                return;
            }

            await TreasuryAccountsEndpoints.RelearnAsync(database, candidate, previous, now, cancellationToken);
            await TreasuryAccountsEndpoints.LearnAsync(database, candidate, newKind, now, cancellationToken);
        }
        else
        {
            candidate.Status = CandidateStatus.Pending;
            candidate.ConfirmedKind = null;
            candidate.ResolvedAt = null;
            await TreasuryAccountsEndpoints.RelearnAsync(database, candidate, previous, now, cancellationToken);
        }
    }

    internal static Task LockOwnerAsync(AppDbContext database, string userId, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({userId}))", cancellationToken);

    private static IResult Missing(Guid id) => Problems.Create(
        StatusCodes.Status404NotFound,
        ProblemCodes.CandidateNotFound,
        $"No payment candidate exists with id {id}.");

    private static IResult Conflict(string code, string title) =>
        Problems.Create(StatusCodes.Status409Conflict, code, title);
}

/// <summary>
/// <c>LinkPaymentId</c> names one of the candidate's matches of <c>Kind</c> to link instead of creating a
/// payment; the period is then the linked payment's own and the one sent is not read.
/// <c>RecordSeparately</c> records a new payment even though the owner typed one with the same date, kind
/// and amount; a <c>LinkPaymentId</c> takes precedence over it.
/// </summary>
internal sealed record ConfirmCandidateRequest(
    PaymentKind Kind,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    Guid? LinkPaymentId,
    bool RecordSeparately = false);

/// <summary>
/// The payment a confirmation recorded or linked. <c>Notice</c> is set when the operation went to another IBAN
/// than the Manual account of its kind and became that kind's learned account: the owner's saved account stays
/// in use, and the notice waits on the Treasury accounts settings until dismissed.
/// </summary>
internal sealed record ConfirmCandidateResponse(PaymentResponse Payment, ManualAccountNotice? Notice);

internal sealed record ManualAccountNotice(string ManualIban);

/// <summary>
/// A pending candidate. <c>SuggestedKind</c> is null when nothing points to one kind. <c>Matches</c> are
/// the payments the owner typed with the same date and amount and no bank operation, of any kind: the
/// one of the kind being confirmed is offered for linking.
/// </summary>
internal sealed record PaymentCandidateResponse(
    Guid Id,
    DateOnly PaidOn,
    long AmountKop,
    string? CounterName,
    string CounterIban,
    string? Purpose,
    PaymentKind? SuggestedKind,
    PaymentMatchResponse[] Matches);

internal sealed record PaymentMatchResponse(
    Guid Id,
    PaymentKind Kind,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    string? Note);
