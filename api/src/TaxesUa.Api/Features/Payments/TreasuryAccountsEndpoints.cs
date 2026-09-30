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
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await FindOrAddAsync(database, user.Id, kind, cancellationToken);
                row.ManualIban = normalized.Iban;
                row.ManualRecipientName = normalized.RecipientName;
                row.ManualRecipientCode = normalized.RecipientCode;
                row.ManualUpdatedAt = time.GetUtcNow();
                row.NoticeAt = null;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(kind, row));
            })
            .Produces<TreasuryAccountResponse>()
            .ProducesValidationProblem()
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
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "No confirmed payment has taught this kind's account yet, so there is nothing to revert to.");
                }

                row.ManualIban = null;
                row.ManualRecipientName = null;
                row.ManualRecipientCode = null;
                row.ManualUpdatedAt = null;
                row.NoticeAt = null;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(kind, row));
            })
            .Produces<TreasuryAccountResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
    /// Records the recipient of a confirmed candidate as the Learned account of <paramref name="kind"/>, in
    /// the caller's transaction and under the owner lock. A Manual account stays in use; a confirmation to
    /// another IBAN than it raises the notice instead.
    /// </summary>
    internal static async Task LearnAsync(
        AppDbContext database,
        BudgetPaymentCandidate candidate,
        PaymentKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await FindOrAddAsync(database, candidate.UserId, kind, cancellationToken);
        var name = string.IsNullOrWhiteSpace(candidate.CounterName) ? null : candidate.CounterName.Trim();
        var code = candidate.CounterEdrpou is { Length: RecipientCodeLength } edrpou && edrpou.All(char.IsAsciiDigit)
            ? edrpou
            : null;

        // The same account confirmed again without a name or code keeps the ones it was learned with.
        if (row.LearnedIban == candidate.CounterIban)
        {
            name ??= row.LearnedRecipientName;
            code ??= row.LearnedRecipientCode;
        }

        row.LearnedIban = candidate.CounterIban;
        row.LearnedRecipientName = name;
        row.LearnedRecipientCode = code;
        row.LearnedExternalId = candidate.ExternalId;
        row.LearnedPaidOn = candidate.PaidOn;
        row.LearnedAt = now;
        if (row.ManualIban is null || row.ManualIban == candidate.CounterIban)
        {
            row.NoticeAt = null;
        }
        else
        {
            row.NoticeAt ??= now;
        }
    }

    internal static bool IsValidTreasuryIban(string iban) =>
        TreasuryPayment.IsTreasury(iban) && InvoicingEndpoints.IsValidUkrainianIban(iban);

    internal static TreasuryAccountRequest Normalize(TreasuryAccountRequest request) => new(
        TreasuryPayment.Normalize(request.Iban) ?? string.Empty,
        request.RecipientName?.Trim() ?? string.Empty,
        request.RecipientCode?.Trim() ?? string.Empty);

    internal static Dictionary<string, string[]>? Validate(TreasuryAccountRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidTreasuryIban(request.Iban))
        {
            errors["iban"] = ["iban must be a Treasury account: a valid Ukrainian IBAN with bank id 899998."];
        }

        if (request.RecipientName.Length == 0)
        {
            errors["recipientName"] = ["recipientName is required."];
        }
        else if (request.RecipientName.Length > MaxManualNameLength)
        {
            errors["recipientName"] = [$"recipientName must not exceed {MaxManualNameLength} characters."];
        }
        else if (TextRules.HasDisallowedControlChar(request.RecipientName))
        {
            errors["recipientName"] = ["recipientName must not contain a control character."];
        }

        if (request.RecipientCode.Length != RecipientCodeLength || !request.RecipientCode.All(char.IsAsciiDigit))
        {
            errors["recipientCode"] = [$"recipientCode must be {RecipientCodeLength} digits."];
        }

        return errors.Count == 0 ? null : errors;
    }

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

    private static TreasuryAccountResponse ToResponse(PaymentKind kind, TreasuryAccount? row)
    {
        if (row is null)
        {
            return new TreasuryAccountResponse(kind, TreasuryAccountSource.None, null, null, null, null, null, false, [], null);
        }

        var source = row.IsManual ? TreasuryAccountSource.Manual
            : row.LearnedIban is not null ? TreasuryAccountSource.Learned
            : TreasuryAccountSource.None;
        var (iban, name, code) = source switch
        {
            TreasuryAccountSource.Manual => (row.ManualIban, row.ManualRecipientName, row.ManualRecipientCode),
            TreasuryAccountSource.Learned => (row.LearnedIban, row.LearnedRecipientName, row.LearnedRecipientCode),
            _ => ((string?)null, null, null),
        };
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

internal sealed record TreasuryAccountRequest(string Iban, string RecipientName, string RecipientCode);

/// <summary>
/// The account in use for one kind. <c>Learned</c> is set when the source is Learned (where it came from);
/// <c>HasLearned</c> tells whether a Manual account can be reverted to a learned one. <c>Notice</c> is the
/// learned account a confirmation went to, while it differs from the Manual one and is not dismissed.
/// <c>Missing</c> names the recipient details the account lacks.
/// </summary>
internal sealed record TreasuryAccountResponse(
    PaymentKind Kind,
    TreasuryAccountSource Source,
    string? Iban,
    string? RecipientName,
    string? RecipientCode,
    DateTimeOffset? UpdatedAt,
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
