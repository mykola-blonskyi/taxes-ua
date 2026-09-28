using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Monobank;

public static class MonobankEndpoints
{
    private static readonly Dictionary<int, string> KnownCurrencyCodes = new()
    {
        [980] = "UAH",
        [840] = "USD",
        [978] = "EUR",
    };

    public static IEndpointRouteBuilder MapMonobankApi(this IEndpointRouteBuilder routes)
    {
        var monobank = routes.MapGroup("/monobank").WithTags("Monobank").RequireAuthorization();

        monobank.MapGet("/connection", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
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

                return await BuildStatusAsync(database, user.Id, cancellationToken);
            })
            .Produces<MonobankConnectionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        monobank.MapPut("/connection", async (
                MonobankTokenRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankClient client,
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
                        await SaveConnectionAsync(database, encryptor, user.Id, request.Token, found.Info, cancellationToken);
                        return await BuildStatusAsync(database, user.Id, cancellationToken);

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
                foreach (var account in accounts)
                {
                    // Only a FOP account can ever be followed; a non-FOP id in the request is silently
                    // ignored rather than accepted and then never synced, which would look like a bug.
                    account.IsActive = account.IsFop && chosen.Contains(account.ExternalId);
                }

                await database.SaveChangesAsync(cancellationToken);

                return await BuildStatusAsync(database, user.Id, cancellationToken);
            })
            .Produces<MonobankConnectionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        monobank.MapDelete("/connection", async (
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

                // Disconnect only ever removes the token; BankAccount rows (and anything imported
                // against them later) stay, per #75's acceptance criteria.
                var connection = await database.MonobankConnections.FindAsync([user.Id], cancellationToken);
                if (connection is not null)
                {
                    database.MonobankConnections.Remove(connection);
                    await database.SaveChangesAsync(cancellationToken);
                }

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static IResult NotConfigured() => Results.Problem(
        title: "monobank is not configured.",
        detail: "MONOBANK_TOKEN_ENCRYPTION_KEY is not set on this deployment, so no token can be "
            + "stored safely.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://taxes-ua/problems/monobank-not-configured");

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
                if (isFop && !stored.IsActive)
                {
                    // Either brand new to being FOP, or reappeared after a previous save dropped it for
                    // being absent: a FOP account the token currently reports is followed by default.
                    stored.IsActive = true;
                }
                else if (!isFop)
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
                // The token's client-info no longer reports this account: stop following it so nothing
                // syncs against data the owner can no longer see or confirm through this token.
                account.IsActive = false;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> BuildStatusAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var connected = await database.MonobankConnections.AnyAsync(connection => connection.UserId == userId, cancellationToken);

        var accounts = await database.BankAccounts
            .Where(account => account.UserId == userId && account.Bank == Bank.Monobank)
            .OrderBy(account => account.ExternalId)
            .ToListAsync(cancellationToken);

        var response = new MonobankConnectionResponse(
            Connected: connected,
            Accounts: [.. accounts.Select(ToResponse)]);

        return Results.Ok(response);
    }

    private static MonobankAccountResponse ToResponse(BankAccount account) => new(
        ExternalId: account.ExternalId,
        Bank: account.Bank,
        Currency: KnownCurrencyCodes.GetValueOrDefault(account.CurrencyCode, account.CurrencyCode.ToString()),
        MaskedIban: MaskIban(account.Iban),
        IsFop: account.IsFop,
        IsSupported: account.IsFop,
        IsFollowed: account.IsActive);

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
    bool IsFollowed);

internal sealed record MonobankConnectionResponse(
    bool Connected,
    IReadOnlyList<MonobankAccountResponse> Accounts);
