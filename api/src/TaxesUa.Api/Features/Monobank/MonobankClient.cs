using System.Net;
using System.Text.Json;

namespace TaxesUa.Api.Features.Monobank;

internal sealed record MonobankAccount(
    string Id,
    string Type,
    int CurrencyCode,
    string Iban);

internal sealed record MonobankClientInfo(
    string ClientId,
    string Name,
    IReadOnlyList<MonobankAccount> Accounts);

// Mirrors NbuLookup in Features/Fx/NbuRateClient.cs: a closed set of outcomes instead of exceptions
// for the ordinary "the token is bad" and "the bank did not answer" cases, so the endpoint can turn
// each into the right HTTP response without a try/catch around business logic.
internal abstract record ClientInfoResult
{
    private ClientInfoResult() { }

    public sealed record Found(MonobankClientInfo Info) : ClientInfoResult;

    // 401 or 403: monobank's shape for "this token is invalid or revoked".
    public sealed record InvalidToken : ClientInfoResult;

    public sealed record Unavailable(string Reason) : ClientInfoResult;
}

/// <summary>
/// The monobank personal API's <c>client-info</c> endpoint, registered as a typed HttpClient like
/// NbuRateClient so tests replace its primary handler instead of reaching the real bank (ADR-011).
/// </summary>
internal sealed class MonobankClient(HttpClient http, ILogger<MonobankClient> logger)
{
    public async Task<ClientInfoResult> GetClientInfoAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "personal/client-info");
        request.Headers.Add("X-Token", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "monobank client-info request failed.");
            return new ClientInfoResult.Unavailable("monobank did not answer.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("monobank did not answer within {Timeout}.", http.Timeout);
            return new ClientInfoResult.Unavailable("monobank did not answer in time.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new ClientInfoResult.InvalidToken();
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("monobank client-info answered {StatusCode}.", (int)response.StatusCode);
                return new ClientInfoResult.Unavailable("monobank answered with an unexpected status.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                return Parse(body);
            }
            // Never the exception's own message in the result: it can quote the response body back,
            // and ClientInfoResult.Unavailable.Reason flows straight into a ProblemDetails response.
            // The exception (never the token, which this code path never sees) is logged instead.
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                logger.LogWarning(exception, "monobank client-info response could not be parsed.");
                return new ClientInfoResult.Unavailable("monobank's response could not be read.");
            }
        }
    }

    private static ClientInfoResult Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"monobank client-info root is {root.ValueKind}, not an object.");
        }

        var clientId = ReadString(root, "clientId") ?? string.Empty;
        var name = ReadString(root, "name") ?? string.Empty;

        var accounts = new List<MonobankAccount>();
        if (root.TryGetProperty("accounts", out var accountsProperty) && accountsProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var account in accountsProperty.EnumerateArray())
            {
                if (account.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException($"monobank client-info account is {account.ValueKind}, not an object.");
                }

                var id = ReadString(account, "id");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // The published enum is black|white|platinum|iron|fop|yellow|eAid, but a real token can
                // return a value outside it (e.g. "diia"). Read as an open string; an unknown value is
                // simply not "fop" and is listed as not supported, never a parse failure.
                var type = ReadString(account, "type") ?? string.Empty;
                var currencyCode = ReadCurrencyCode(account);
                var iban = ReadString(account, "iban") ?? string.Empty;

                accounts.Add(new MonobankAccount(id, type, currencyCode, iban));
            }
        }

        return new ClientInfoResult.Found(new MonobankClientInfo(clientId, name, accounts));
    }

    // Absent or null reads as "not sent" (the caller defaults it); present with the wrong JSON kind is
    // a shape monobank should never send, so it fails the whole response instead of coercing silently.
    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"monobank client-info \"{property}\" is {value.ValueKind}, not a string.");
        }

        return value.GetString();
    }

    private static int ReadCurrencyCode(JsonElement account)
    {
        if (!account.TryGetProperty("currencyCode", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var currencyCode))
        {
            throw new FormatException("monobank client-info \"currencyCode\" is not a valid 32-bit number.");
        }

        return currencyCode;
    }
}
