using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

public static class TreasuryAccountsEndpoints
{
    // The NBU credit-transfer QR limits the recipient name to 140 characters (spec #97); a learned name is
    // whatever the bank sent and is only shortened to the column.
    internal const int MaxManualNameLength = 140;

    internal const int RecipientCodeLength = 8;

    // An RNOKPP is 10 digits: a private counterparty's code, kept on the candidate as sent.
    internal const int MaxEdrpouLength = 10;

    private static readonly PaymentKind[] Kinds = [PaymentKind.SingleTax, PaymentKind.MilitaryLevy, PaymentKind.Esv];

    public static IEndpointRouteBuilder MapTreasuryAccountsApi(this IEndpointRouteBuilder routes)
    {
        var accounts = routes.MapGroup("/settings/treasury-accounts").WithTags("Treasury accounts").RequireAuthorization();

        accounts.MapGet("", async (
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

                var rows = await database.TreasuryAccounts.AsNoTracking()
                    .Where(row => row.UserId == user.Id)
                    .ToListAsync(cancellationToken);

                return Results.Ok(Kinds.Select(kind => ToResponse(kind, rows.FirstOrDefault(row => row.Kind == kind))).ToArray());
            })
            .Produces<TreasuryAccountResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        accounts.MapPut("/{kind}", async (
                PaymentKind kind,
                TreasuryAccountRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(kind))
                {
                    return Results.NotFound();
                }

                var normalized = Normalize(request);
                if (Validate(normalized) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await FindOrAddAsync(database, user.Id, kind, cancellationToken);
                var (previousManualIban, previousManualEnd) = (row.ManualIban, row.ManualValidUntil);
                row.ManualIban = normalized.Iban;
                row.ManualRecipientName = normalized.RecipientName;
                row.ManualRecipientCode = normalized.RecipientCode;
                row.ManualUpdatedAt = time.GetUtcNow();
                // A new IBAN is a new account and starts with no end. Without an explicit end, the same IBAN
                // keeps the end it had, as Manual or as the Learned account it is being entered over.
                row.ManualValidUntil = normalized.ValidUntil
                    ?? (normalized.Iban == previousManualIban ? previousManualEnd
                        : normalized.Iban == row.LearnedIban ? row.LearnedValidUntil : null);
                row.NoticeAt = null;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(kind, row));
            })
            .Produces<TreasuryAccountResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        accounts.MapPost("/{kind}/revert", async (
                PaymentKind kind,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(kind))
                {
                    return Results.NotFound();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await database.TreasuryAccounts
                    .FirstOrDefaultAsync(account => account.UserId == user.Id && account.Kind == kind, cancellationToken);
                if (row is not { LearnedIban: not null })
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.TreasuryNothingToRevert,
                        "No confirmed payment has taught this kind's account yet, so there is nothing to revert to.");
                }

                row.ManualIban = null;
                row.ManualRecipientName = null;
                row.ManualRecipientCode = null;
                row.ManualUpdatedAt = null;
                row.ManualValidUntil = null;
                row.NoticeAt = null;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(kind, row));
            })
            .Produces<TreasuryAccountResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        accounts.MapPut("/{kind}/valid-until", async (
                PaymentKind kind,
                TreasuryAccountValidUntilRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(kind))
                {
                    return Results.NotFound();
                }

                if (ValidUntilProblem(request.ValidUntil) is { } problem)
                {
                    return Problems.Validation("validUntil", problem.Code, problem.Message);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await database.TreasuryAccounts
                    .FirstOrDefaultAsync(account => account.UserId == user.Id && account.Kind == kind, cancellationToken);
                if (row is null || InUse(row).Source == TreasuryAccountSource.None)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.TreasuryNothingToEnd,
                        "There is no account for this kind yet, so there is nothing to end.");
                }

                if (row.IsManual)
                {
                    row.ManualValidUntil = request.ValidUntil;
                }
                else
                {
                    row.LearnedValidUntil = request.ValidUntil;
                }

                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(kind, row));
            })
            .Produces<TreasuryAccountResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{kind}/notice/dismiss", async (
                PaymentKind kind,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(kind))
                {
                    return Results.NotFound();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await database.TreasuryAccounts
                    .FirstOrDefaultAsync(account => account.UserId == user.Id && account.Kind == kind, cancellationToken);
                if (row is { NoticeAt: not null })
                {
                    row.NoticeAt = null;
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    /// <summary>
    /// Teaches the Learned account of <paramref name="kind"/> the recipient of a confirmed candidate, in the
    /// caller's transaction and under the owner lock. The latest operation wins (Rule 12): an older one only
    /// fills a name or code the same IBAN lacks. A Manual account stays in use; an operation that becomes the
    /// Learned account with another IBAN than it raises the notice instead.
    /// </summary>
    internal static async Task<TreasuryAccount> LearnAsync(
        AppDbContext database,
        BudgetPaymentCandidate candidate,
        PaymentKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await FindOrAddAsync(database, candidate.UserId, kind, cancellationToken);
        if (Teach(row, candidate, now))
        {
            row.NoticeAt = row.ManualIban is null || row.ManualIban == candidate.CounterIban ? null : row.NoticeAt ?? now;
        }

        return row;
    }

    /// <summary>
    /// Learns the account of <paramref name="kind"/> again when <paramref name="source"/>, a confirmation of the
    /// Learned IBAN, has since changed: its payment was deleted, it now pays another kind, or it gained a code.
    /// Any such confirmation counts, not only the one learned from, because an older one of the same IBAN may
    /// have filled in the name or code the account holds. The
    /// owner's confirmations of the kind, as this unit of work leaves them, are taught again in order; none
    /// left clears the Learned account. A notice stays only while the Learned IBAN differs from the Manual one.
    /// </summary>
    internal static async Task RelearnAsync(
        AppDbContext database,
        BudgetPaymentCandidate source,
        PaymentKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await database.TreasuryAccounts
            .FirstOrDefaultAsync(account => account.UserId == source.UserId && account.Kind == kind, cancellationToken);
        if (row is null || row.LearnedIban != source.CounterIban)
        {
            return;
        }

        // Filtered again in memory: the query reads stored rows, and the source's own change is not saved yet.
        var confirmations = (await database.BudgetPaymentCandidates
                .Where(candidate => candidate.UserId == source.UserId && candidate.ConfirmedKind == kind)
                .ToListAsync(cancellationToken))
            .Where(candidate => candidate.Status == CandidateStatus.Confirmed && candidate.ConfirmedKind == kind)
            .OrderBy(candidate => candidate.PaidOn)
            .ThenBy(candidate => candidate.ResolvedAt);
        var endedOn = row.LearnedValidUntil;
        row.LearnedIban = null;
        row.LearnedRecipientName = null;
        row.LearnedRecipientCode = null;
        row.LearnedExternalId = null;
        row.LearnedPaidOn = null;
        row.LearnedAt = null;
        row.LearnedValidUntil = null;
        var standing = confirmations.ToList();
        foreach (var candidate in standing)
        {
            Teach(row, candidate, now);
        }

        // Another IBAN confirmed in between breaks the carrying of a name or code from one confirmation to the
        // next, so what the Learned IBAN still lacks is taken from its own confirmations, newest first (Rule 12).
        foreach (var candidate in standing
                     .Where(candidate => candidate.CounterIban == row.LearnedIban)
                     .OrderByDescending(candidate => candidate.PaidOn)
                     .ThenByDescending(candidate => candidate.ResolvedAt))
        {
            row.LearnedRecipientName ??= NameOf(candidate);
            row.LearnedRecipientCode ??= CodeOf(candidate);
        }

        // The end the owner gave belongs to the IBAN, so it survives only while that IBAN is the one learned.
        if (row.LearnedIban == source.CounterIban)
        {
            row.LearnedValidUntil = endedOn;
        }

        if (row.LearnedIban is null || row.ManualIban is null || row.ManualIban == row.LearnedIban)
        {
            row.NoticeAt = null;
        }
    }

    private static string? NameOf(BudgetPaymentCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.CounterName) ? null : candidate.CounterName.Trim();

    private static string? CodeOf(BudgetPaymentCandidate candidate) =>
        candidate.CounterEdrpou is { Length: RecipientCodeLength } edrpou && edrpou.All(char.IsAsciiDigit) ? edrpou : null;

    // True when the candidate became the Learned account: it is no older than the operation learned before,
    // and a later confirmation of the same day wins.
    private static bool Teach(TreasuryAccount row, BudgetPaymentCandidate candidate, DateTimeOffset now)
    {
        var name = NameOf(candidate);
        var code = CodeOf(candidate);
        var sameIban = row.LearnedIban == candidate.CounterIban;
        if (row.LearnedPaidOn is { } learnedOn && candidate.PaidOn < learnedOn)
        {
            if (sameIban)
            {
                row.LearnedRecipientName ??= name;
                row.LearnedRecipientCode ??= code;
            }

            return false;
        }

        if (sameIban)
        {
            name ??= row.LearnedRecipientName;
            code ??= row.LearnedRecipientCode;
        }

        if (!sameIban)
        {
            row.LearnedValidUntil = null;
        }

        row.LearnedIban = candidate.CounterIban;
        row.LearnedRecipientName = name;
        row.LearnedRecipientCode = code;
        row.LearnedExternalId = candidate.ExternalId;
        row.LearnedPaidOn = candidate.PaidOn;
        row.LearnedAt = now;
        return true;
    }

    internal static TreasuryAccountRequest Normalize(TreasuryAccountRequest request) => new(
        TreasuryPayment.Normalize(request.Iban) ?? string.Empty,
        request.RecipientName?.Trim() ?? string.Empty,
        request.RecipientCode?.Trim() ?? string.Empty,
        request.ValidUntil);

    internal static FieldErrors? Validate(TreasuryAccountRequest request)
    {
        var errors = new FieldErrors();
        if (InvoicingEndpoints.IbanProblem(request.Iban, TreasuryPayment.TreasuryBankId) is { } ibanProblem)
        {
            errors.Set("iban", ibanProblem);
        }

        if (request.RecipientName.Length == 0)
        {
            errors.Set("recipientName", ProblemCodes.Required, "recipientName is required.");
        }
        else if (request.RecipientName.Length > MaxManualNameLength)
        {
            errors.Set(
                "recipientName",
                ProblemCodes.TooLong,
                $"recipientName must not exceed {MaxManualNameLength} characters.");
        }
        else if (TextRules.HasDisallowedControlChar(request.RecipientName))
        {
            errors.Set(
                "recipientName",
                ProblemCodes.ControlCharacter,
                "recipientName must not contain a control character.");
        }

        if (request.RecipientCode.Length != RecipientCodeLength || !request.RecipientCode.All(char.IsAsciiDigit))
        {
            errors.Set(
                "recipientCode",
                ProblemCodes.RecipientCodeInvalid,
                $"recipientCode must be {RecipientCodeLength} digits.");
        }

        if (ValidUntilProblem(request.ValidUntil) is { } validUntilProblem)
        {
            errors.Set("validUntil", validUntilProblem);
        }

        return errors.OrNull();
    }

    internal static Issue? ValidUntilProblem(DateOnly? validUntil) =>
        validUntil is { } date && !PaymentsEndpoints.InYearRange(date.Year)
            ? new Issue(ProblemCodes.YearOutOfRange, PaymentsEndpoints.YearRangeMessage("validUntil year"))
            : null;

    private static async Task<TreasuryAccount> FindOrAddAsync(
        AppDbContext database, string userId, PaymentKind kind, CancellationToken cancellationToken)
    {
        var row = await database.TreasuryAccounts
            .FirstOrDefaultAsync(account => account.UserId == userId && account.Kind == kind, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new TreasuryAccount { Id = Guid.NewGuid(), UserId = userId, Kind = kind };
        database.TreasuryAccounts.Add(row);
        return row;
    }

    internal static (TreasuryAccountSource Source, string? Iban, string? Name, string? Code) InUse(TreasuryAccount row) =>
        row.IsManual ? (TreasuryAccountSource.Manual, row.ManualIban, row.ManualRecipientName, row.ManualRecipientCode)
        : row.LearnedIban is not null ? (TreasuryAccountSource.Learned, row.LearnedIban, row.LearnedRecipientName, row.LearnedRecipientCode)
        : (TreasuryAccountSource.None, null, null, null);

    /// <summary>The last day the account in use can receive a payment, or null when it has no end.</summary>
    internal static DateOnly? ValidUntilOf(TreasuryAccount row) =>
        row.IsManual ? row.ManualValidUntil : row.LearnedIban is not null ? row.LearnedValidUntil : null;

    private static TreasuryAccountResponse ToResponse(PaymentKind kind, TreasuryAccount? row)
    {
        if (row is null)
        {
            return new TreasuryAccountResponse(kind, TreasuryAccountSource.None, null, null, null, null, null, null, false, [], null);
        }

        var (source, iban, name, code) = InUse(row);
        string[] missing = source == TreasuryAccountSource.None
            ? []
            : [.. new[] { name is null ? "recipientName" : null, code is null ? "recipientCode" : null }.OfType<string>()];
        var learned = row.LearnedIban is null
            ? null
            : new TreasuryAccountLearned(
                row.LearnedIban,
                row.LearnedRecipientName,
                row.LearnedRecipientCode,
                row.LearnedExternalId!,
                row.LearnedPaidOn!.Value,
                row.LearnedAt!.Value);

        return new TreasuryAccountResponse(
            kind,
            source,
            iban,
            name,
            code,
            source == TreasuryAccountSource.Manual ? row.ManualUpdatedAt : row.LearnedAt,
            ValidUntilOf(row),
            source == TreasuryAccountSource.Learned ? learned : null,
            learned is not null,
            missing,
            row.NoticeAt is not null ? learned : null);
    }
}

internal enum TreasuryAccountSource
{
    None,
    Learned,
    Manual,
}

internal sealed record TreasuryAccountRequest(string Iban, string RecipientName, string RecipientCode, DateOnly? ValidUntil = null);

internal sealed record TreasuryAccountValidUntilRequest(DateOnly? ValidUntil);

/// <summary>
/// The account in use for one kind. <c>Learned</c> is set when the source is Learned (where it came from);
/// <c>HasLearned</c> tells whether a Manual account can be reverted to a learned one. <c>Notice</c> is the
/// learned account a confirmation went to, while it differs from the Manual one and is not dismissed.
/// <c>Missing</c> names the recipient details the account lacks. <c>ValidUntil</c> is the last day the
/// account in use can receive a payment (Rule 16), null when it has no end.
/// </summary>
internal sealed record TreasuryAccountResponse(
    PaymentKind Kind,
    TreasuryAccountSource Source,
    string? Iban,
    string? RecipientName,
    string? RecipientCode,
    DateTimeOffset? UpdatedAt,
    DateOnly? ValidUntil,
    TreasuryAccountLearned? Learned,
    bool HasLearned,
    string[] Missing,
    TreasuryAccountLearned? Notice);

internal sealed record TreasuryAccountLearned(
    string Iban,
    string? RecipientName,
    string? RecipientCode,
    string OperationId,
    DateOnly PaidOn,
    DateTimeOffset LearnedAt);
