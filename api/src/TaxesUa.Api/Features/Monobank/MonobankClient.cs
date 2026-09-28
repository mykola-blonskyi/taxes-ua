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
internal sealed class MonobankClient(HttpClient http)
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
            return new ClientInfoResult.Unavailable($"monobank request failed: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ClientInfoResult.Unavailable($"monobank did not answer within {http.Timeout}.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new ClientInfoResult.InvalidToken();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ClientInfoResult.Unavailable($"monobank answered {(int)response.StatusCode}.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Parse(body);
            }
            catch (JsonException exception)
            {
                return new ClientInfoResult.Unavailable($"monobank's body is not usable: {exception.Message}");
            }
        }
    }

    private static ClientInfoResult Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var clientId = root.TryGetProperty("clientId", out var clientIdProperty)
            ? clientIdProperty.GetString() ?? string.Empty
            : string.Empty;
        var name = root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? string.Empty : string.Empty;

        var accounts = new List<MonobankAccount>();
        if (root.TryGetProperty("accounts", out var accountsProperty) && accountsProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var account in accountsProperty.EnumerateArray())
            {
                var id = account.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // The published enum is black|white|platinum|iron|fop|yellow|eAid, but a real token can
                // return a value outside it (e.g. "diia"). Read as an open string; an unknown value is
                // simply not "fop" and is listed as not supported, never a parse failure.
                var type = account.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() ?? string.Empty : string.Empty;
                var currencyCode = account.TryGetProperty("currencyCode", out var currencyProperty)
                    && currencyProperty.ValueKind == JsonValueKind.Number
                        ? currencyProperty.GetInt32()
                        : 0;
                var iban = account.TryGetProperty("iban", out var ibanProperty) ? ibanProperty.GetString() ?? string.Empty : string.Empty;

                accounts.Add(new MonobankAccount(id, type, currencyCode, iban));
            }
        }

        return new ClientInfoResult.Found(new MonobankClientInfo(clientId, name, accounts));
    }
}
