using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// The invoicing form's "fill from monobank": the seller name and the FOP accounts' payment details,
/// served under the invoicing settings route.
/// </summary>
public static class InvoicingPrefillEndpoints
{
    // Every monobank account is held at Universal Bank, so these are constants rather than data read
    // from the bank (issue #91).
    internal const string MonobankBeneficiaryBank = "JSC Universal Bank, Kyiv";

    internal const string MonobankSwift = "UNJSUAUKXXX";

    public static IEndpointRouteBuilder MapInvoicingPrefillApi(this IEndpointRouteBuilder routes)
    {
        var invoicing = routes.MapGroup("/settings/invoicing").WithTags("Invoicing").RequireAuthorization();

        // A read that answers suggestions and stores nothing: the owner saves them, or does not.
        invoicing.MapPost("/prefill-from-monobank", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankClientInfoReader reader,
                MonobankClient client,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var generation = reader.Generation(user.Id);
                var connection = await database.MonobankConnections.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                if (connection is null || connection.RejectedAt is not null || !encryptor.IsConfigured)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.MonobankNotConnected,
                        "monobank is not connected.");
                }

                string token;
                try
                {
                    token = encryptor.Decrypt(connection.EncryptedToken, connection.UserId);
                }
                catch (CryptographicException)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.MonobankTokenUnreadable,
                        "The stored monobank token cannot be read. Connect monobank again.");
                }

                // The accounts are the stored rows; only the name needs the bank. The reader shares its answer
                // with the token save and the jar reads, and a request must not park for up to a minute on the
                // slot, so a busy one is answered at once with how long to wait.
                var read = await reader.ReadAsync(client, user.Id, token, generation, cancellationToken);
                if (read is ClientInfoRead.Waiting waiting)
                {
                    var seconds = (int)Math.Ceiling(waiting.RetryAfter.TotalSeconds);
                    http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    return Problems.Create(
                        StatusCodes.Status429TooManyRequests,
                        ProblemCodes.MonobankRateLimited,
                        "monobank allows one request a minute.",
                        detail: $"Try again in {seconds} seconds.");
                }

                if (read is ClientInfoRead.InvalidToken)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.MonobankTokenRejected,
                        "monobank rejected the token. Connect monobank again.");
                }

                if (read is not ClientInfoRead.Found found)
                {
                    return Problems.Create(
                        StatusCodes.Status502BadGateway,
                        ProblemCodes.MonobankUnavailable,
                        "monobank could not be reached for the name.");
                }

                var accounts = await database.BankAccounts.AsNoTracking()
                    .Where(account => account.UserId == user.Id && account.Bank == Bank.Monobank && account.IsFop)
                    .OrderByDescending(account => account.IsActive)
                    .ThenBy(account => account.ExternalId)
                    .ToListAsync(cancellationToken);

                var suggestions = new List<PaymentDetailsInput>();
                foreach (var account in accounts)
                {
                    if (IsoCurrency.FromNumeric(account.CurrencyCode) is not { } currency
                        || suggestions.Exists(suggestion => suggestion.Currency == currency))
                    {
                        continue;
                    }

                    suggestions.Add(new PaymentDetailsInput(
                        currency,
                        account.Iban,
                        MonobankBeneficiaryBank,
                        MonobankSwift,
                        string.Empty,
                        string.Empty,
                        string.Empty));
                }

                return Results.Ok(new MonobankPrefillResponse(
                    found.Name.Trim(), [.. suggestions.OrderBy(suggestion => suggestion.Currency)]));
            })
            .Produces<MonobankPrefillResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status409Conflict)
            .ProducesCodedProblem(StatusCodes.Status429TooManyRequests)
            .ProducesCodedProblem(StatusCodes.Status502BadGateway);

        return routes;
    }
}

internal sealed record MonobankPrefillResponse(string SellerNameUk, PaymentDetailsInput[] PaymentDetails);
