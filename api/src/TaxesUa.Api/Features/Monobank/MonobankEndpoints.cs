using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Monobank;

public static class MonobankEndpoints
{
    public static IEndpointRouteBuilder MapMonobankApi(this IEndpointRouteBuilder routes)
    {
        var monobank = routes.MapGroup("/monobank").WithTags("Monobank").RequireAuthorization();

        monobank.MapGet("/connection", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankSyncQueue queue,
                MonobankWebhooks webhooks,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!encryptor.IsConfigured)
                {
                    return NotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(await LoadStatusAsync(database, queue, webhooks, time, user.Id, cancellationToken));
            })
            .Produces<MonobankConnectionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        monobank.MapPut("/connection", async (
                MonobankTokenRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankSyncQueue queue,
                MonobankWebhooks webhooks,
                MonobankClient client,
                MonobankClientInfoReader reader,
                MonobankRateGate gate,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!encryptor.IsConfigured)
                {
                    return NotConfigured();
                }

                if (string.IsNullOrWhiteSpace(request.Token))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["token"] = ["Token is required."],
                    });
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var result = await client.GetClientInfoAsync(request.Token, cancellationToken);
                // The check above is not gated (the token must be validated now), so whatever it returned,
                // the next gated read starts a full interval after it.
                gate.Mark(user.Id, MonobankClientInfoReader.Method);
                switch (result)
                {
                    case ClientInfoResult.InvalidToken:
                        // Nothing is stored: the token is checked before it ever reaches encryption or
                        // the database, so a typo never lands even encrypted.
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["token"] = ["monobank rejected this token."],
                        });

                    case ClientInfoResult.Unavailable unavailable:
                        return Results.Problem(
                            title: "monobank is temporarily unavailable.",
                            detail: unavailable.Reason,
                            statusCode: StatusCodes.Status502BadGateway);

                    case ClientInfoResult.Found found:
                        await SaveConnectionAsync(
                            database, encryptor, user.Id, request.Token, found.Info, cancellationToken);
                        await EnqueueFollowedAsync(database, queue, user.Id, cancellationToken);
                        webhooks.Reconcile(user.Id);
                        // This call spent the client-info slot; its answer serves the next minute's jar reads and prefill.
                        reader.Remember(user.Id, found.Info);

                        return Results.Ok(await LoadStatusAsync(database, queue, webhooks, time, user.Id, cancellationToken));

                    default:
                        throw new InvalidOperationException($"Unhandled {nameof(ClientInfoResult)}.");
                }
            })
            .Produces<MonobankConnectionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        monobank.MapPut("/accounts", async (
                FollowedAccountsRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankSyncQueue queue,
                MonobankWebhooks webhooks,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!encryptor.IsConfigured)
                {
                    return NotConfigured();
                }

                if (request.FollowedExternalIds is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["followedExternalIds"] = ["followedExternalIds is required."],
                    });
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var accounts = await database.BankAccounts
                    .Where(account => account.UserId == user.Id && account.Bank == Bank.Monobank)
                    .ToListAsync(cancellationToken);

                var chosen = new HashSet<string>(request.FollowedExternalIds, StringComparer.Ordinal);
                var newlyFollowed = new List<Guid>();
                foreach (var account in accounts)
                {
                    // Only a FOP account can ever be followed; a non-FOP id in the request is silently
                    // ignored rather than accepted and then never synced, which would look like a bug.
                    var follow = account.IsFop && chosen.Contains(account.ExternalId);
                    if (follow && !account.IsActive)
                    {
                        newlyFollowed.Add(account.Id);
                    }

                    account.IsActive = follow;
                }

                await database.SaveChangesAsync(cancellationToken);
                foreach (var accountId in newlyFollowed)
                {
                    queue.Enqueue(new SyncWork(user.Id, accountId));
                }

                return Results.Ok(await LoadStatusAsync(database, queue, webhooks, time, user.Id, cancellationToken));
            })
            .Produces<MonobankConnectionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        monobank.MapDelete("/connection", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                MonobankWebhooks webhooks,
                MonobankClientInfoReader reader,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                reader.Forget(user.Id);

                // Disconnect only ever removes the token; BankAccount rows (and anything imported
                // against them later) stay, per #75's acceptance criteria. So does the reserve jar's
                // last balance, which keeps its time.
                var connection = await database.MonobankConnections.FindAsync([user.Id], cancellationToken);
                if (connection is not null)
                {
                    database.MonobankConnections.Remove(connection);
                    await database.SaveChangesAsync(cancellationToken);
                    if (webhooks.IsConfigured || connection.WebhookUrl is not null)
                    {
                        webhooks.Clear(user.Id, connection.EncryptedToken);
                    }
                }

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        // Only enqueues: MonobankSyncWorker reads the bank, so this request never waits on it.
        monobank.MapPost("/sync", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankSyncQueue queue,
                MonobankWebhooks webhooks,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!encryptor.IsConfigured)
                {
                    return NotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var connection = await database.MonobankConnections.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                if (connection is null)
                {
                    return Results.Problem(
                        title: "Connect monobank before syncing.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                if (connection.RejectedAt is not null)
                {
                    return Results.Problem(
                        title: "monobank rejected the token; replace it before syncing.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                await EnqueueFollowedAsync(database, queue, user.Id, cancellationToken);

                return Results.Accepted(
                    "/api/monobank/connection", await LoadStatusAsync(database, queue, webhooks, time, user.Id, cancellationToken));
            })
            .Produces<MonobankConnectionResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // monobank's own endpoints (ADR-012): anonymous, found only by the secret in the path, and
        // never reading the body, so a forged notification can at most queue a statement read.
        routes.MapGet(MonobankWebhooks.Route + "{secret}", async (
                string secret, AppDbContext database, CancellationToken cancellationToken) =>
                await OwnerOfSecretAsync(database, secret, cancellationToken) is null ? Results.NotFound() : Results.Ok())
            .AllowAnonymous()
            .ExcludeFromDescription();

        routes.MapPost(MonobankWebhooks.Route + "{secret}", async (
                string secret, AppDbContext database, MonobankSyncQueue queue, CancellationToken cancellationToken) =>
            {
                if (await OwnerOfSecretAsync(database, secret, cancellationToken) is not { } ownerId)
                {
                    return Results.NotFound();
                }

                await EnqueueFollowedAsync(database, queue, ownerId, cancellationToken);
                return Results.Ok();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        return routes;
    }

    private static Task<string?> OwnerOfSecretAsync(AppDbContext database, string secret, CancellationToken cancellationToken) =>
        database.MonobankConnections
            .Where(row => row.WebhookSecret == secret)
            .Select(row => row.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    private static IResult NotConfigured() => Results.Problem(
        title: "monobank is not configured.",
        detail: "MONOBANK_TOKEN_ENCRYPTION_KEY is not set on this deployment, so no token can be "
            + "stored safely.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://taxes-ua/problems/monobank-not-configured");

    internal static async Task EnqueueFollowedAsync(
        AppDbContext database, MonobankSyncQueue queue, string userId, CancellationToken cancellationToken)
    {
        var followed = await database.BankAccounts
            .Where(account => account.UserId == userId
                && account.Bank == Bank.Monobank
                && account.IsFop
                && account.IsActive)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
        foreach (var accountId in followed)
        {
            queue.Enqueue(new SyncWork(userId, accountId));
        }
    }

    private static async Task SaveConnectionAsync(
        AppDbContext database,
        TokenEncryptor encryptor,
        string userId,
        string token,
        MonobankClientInfo info,
        CancellationToken cancellationToken)
    {
        var connection = await database.MonobankConnections.FindAsync([userId], cancellationToken);
        if (connection is null)
        {
            connection = new MonobankConnection { UserId = userId };
            database.MonobankConnections.Add(connection);
        }

        connection.EncryptedToken = encryptor.Encrypt(token);
        connection.MonobankClientId = info.ClientId;
        connection.ConnectedAt = DateTimeOffset.UtcNow;
        connection.RejectedAt = null;
        connection.WebhookSecret = MonobankWebhooks.NewSecret();
        connection.WebhookFailedAt = null;
        connection.WebhookFailure = null;
        // A rejection or failed registration written by a worker after this row was read must still be
        // cleared, so the columns are written even when the tracked values were already null.
        if (database.Entry(connection).State != EntityState.Added)
        {
            database.Entry(connection).Property(row => row.RejectedAt).IsModified = true;
            database.Entry(connection).Property(row => row.WebhookFailedAt).IsModified = true;
            database.Entry(connection).Property(row => row.WebhookFailure).IsModified = true;
        }

        var existing = await database.BankAccounts
            .Where(account => account.UserId == userId && account.Bank == Bank.Monobank)
            .ToDictionaryAsync(account => account.ExternalId, cancellationToken);

        var seenExternalIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var account in info.Accounts)
        {
            seenExternalIds.Add(account.Id);
            var isFop = account.Type == "fop";

            if (existing.TryGetValue(account.Id, out var stored))
            {
                stored.Name = info.Name;
                stored.CurrencyCode = account.CurrencyCode;
                stored.Iban = account.Iban;
                stored.AccountType = account.Type;
                stored.IsFop = isFop;
                // The owner's follow choice stands; only a brand new account gets the default.
                if (!isFop)
                {
                    stored.IsActive = false;
                }
            }
            else
            {
                database.BankAccounts.Add(new BankAccount
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Bank = Bank.Monobank,
                    ExternalId = account.Id,
                    Name = info.Name,
                    CurrencyCode = account.CurrencyCode,
                    Iban = account.Iban,
                    AccountType = account.Type,
                    IsFop = isFop,
                    // FOP accounts are preselected for sync, per #75; every other type is offered but
                    // starts (and, via MapPut("/accounts"), stays) unfollowed.
                    IsActive = isFop,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        foreach (var account in existing.Values)
        {
            if (!seenExternalIds.Contains(account.ExternalId))
            {
                // A sync must never read an account this token cannot see.
                account.IsActive = false;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<MonobankConnectionResponse> LoadStatusAsync(
        AppDbContext database,
        MonobankSyncQueue queue,
        MonobankWebhooks webhooks,
        TimeProvider time,
        string userId,
        CancellationToken cancellationToken)
    {
        var connection = await database.MonobankConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var registeredOn = await database.Settings
            .Where(row => row.UserId == userId)
            .Select(row => row.FopRegistrationDate)
            .FirstOrDefaultAsync(cancellationToken);
        var accounts = await database.BankAccounts
            .Where(account => account.UserId == userId && account.Bank == Bank.Monobank)
            .OrderBy(account => account.ExternalId)
            .ToListAsync(cancellationToken);

        var lastBatches = await database.ImportBatches
            .Where(batch => batch.UserId == userId)
            .GroupBy(batch => batch.BankAccountId)
            .Select(batches => batches.OrderByDescending(batch => batch.CreatedAt).First())
            .ToDictionaryAsync(batch => batch.BankAccountId, cancellationToken);

        return new MonobankConnectionResponse(
            Connected: connection is not null,
            TokenRejectedAt: connection?.RejectedAt,
            Webhook: connection is null
                ? null
                : new WebhookStatusResponse(
                    webhooks.StateOf(connection),
                    connection is { WebhookFailedAt: { } webhookFailedAt, WebhookFailure: { } webhookFailure }
                        ? new SyncFailureResponse(webhookFailedAt, webhookFailure)
                        : null),
            BackfillStart: new BackfillStartResponse(
                MonobankStatementImport.BackfillStart(registeredOn, time.TodayInKyiv()),
                FromRegistrationDate: registeredOn is not null),
            Accounts: [.. accounts.Select(account => ToResponse(
                account,
                queue.IsPending(new SyncWork(userId, account.Id)),
                lastBatches.GetValueOrDefault(account.Id)))]);
    }

    private static MonobankAccountResponse ToResponse(
        BankAccount account, bool syncPending, ImportBatch? lastSync) => new(
        ExternalId: account.ExternalId,
        Bank: account.Bank,
        Currency: IsoCurrency.Display(account.CurrencyCode),
        MaskedIban: MaskIban(account.Iban),
        IsFop: account.IsFop,
        IsSupported: account.IsFop,
        IsFollowed: account.IsActive,
        SyncPending: syncPending,
        LastSync: lastSync is null
            ? null
            : new LastSyncResponse(lastSync.CreatedAt, lastSync.From, lastSync.To, lastSync.ImportedCount, lastSync.SkippedCount),
        SyncedThrough: account.SyncedThrough,
        BackfillComplete: account.HistoryImportedAt is not null,
        LastFailure: account is { LastFailedAt: { } failedAt, LastFailure: { } failure }
            ? new SyncFailureResponse(failedAt, failure)
            : null);

    // Keeps only the last 4 characters, e.g. "UA•••••••••••••••••••1234", so the owner can recognise
    // an account without the full IBAN sitting in a response or on screen.
    internal static string MaskIban(string iban)
    {
        if (string.IsNullOrEmpty(iban))
        {
            return string.Empty;
        }

        const int visibleSuffix = 4;
        if (iban.Length <= visibleSuffix)
        {
            return iban;
        }

        return new string('•', iban.Length - visibleSuffix) + iban[^visibleSuffix..];
    }
}

internal sealed record MonobankTokenRequest(string Token);

internal sealed record FollowedAccountsRequest(IReadOnlyList<string> FollowedExternalIds);

internal sealed record MonobankAccountResponse(
    string ExternalId,
    Bank Bank,
    string Currency,
    string MaskedIban,
    bool IsFop,
    bool IsSupported,
    bool IsFollowed,
    bool SyncPending,
    LastSyncResponse? LastSync,
    DateTimeOffset? SyncedThrough,
    bool BackfillComplete,
    SyncFailureResponse? LastFailure);

internal sealed record SyncFailureResponse(DateTimeOffset At, SyncFailure Reason);

// LastFailure is the last failed registration, kept until one succeeds, so it can accompany Pending.
internal sealed record WebhookStatusResponse(WebhookState State, SyncFailureResponse? LastFailure);

// The day an account's first sync reads from: the FOP registration date, or 1 January of the current
// year when settings have none.
internal sealed record BackfillStartResponse(DateOnly From, bool FromRegistrationDate);

internal sealed record LastSyncResponse(
    DateTimeOffset At,
    DateTimeOffset From,
    DateTimeOffset To,
    int ImportedCount,
    int SkippedCount);

internal sealed record MonobankConnectionResponse(
    bool Connected,
    DateTimeOffset? TokenRejectedAt,
    WebhookStatusResponse? Webhook,
    BackfillStartResponse BackfillStart,
    IReadOnlyList<MonobankAccountResponse> Accounts);
