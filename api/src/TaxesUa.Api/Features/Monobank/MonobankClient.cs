using System.Diagnostics;
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

// One statement operation. Time is the bank's instant; Amount is in the account currency's minor units
// and negative for a debit.
internal sealed record MonobankStatementItem(
    string Id,
    DateTimeOffset Time,
    string? Description,
    long Amount,
    int CurrencyCode,
    bool Hold,
    string? Comment,
    string? CounterName);

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

internal abstract record StatementResult
{
    private StatementResult() { }

    public sealed record Found(IReadOnlyList<MonobankStatementItem> Items) : StatementResult;

    public sealed record InvalidToken : StatementResult;

    public sealed record Unavailable(string Reason) : StatementResult;
}

/// <summary>
/// The monobank personal API's <c>client-info</c> and <c>statement</c> endpoints, registered as a typed
/// HttpClient like NbuRateClient so tests replace its primary handler instead of reaching the real bank
/// (ADR-011). Answers are parsed into records here; no business rule lives in this class.
/// </summary>
internal sealed class MonobankClient(HttpClient http, ILogger<MonobankClient> logger)
{
    // monobank answers at most this many operations per statement call; a full page means older ones remain.
    public const int StatementPageSize = 500;

    private const string Unreadable = "monobank's response could not be read.";

    public async Task<ClientInfoResult> GetClientInfoAsync(string token, CancellationToken cancellationToken) =>
        await GetAsync("personal/client-info", token, "client-info", cancellationToken) switch
        {
            Answer.Body body => Read(body.Text, "client-info", ParseClientInfo)
                ?? new ClientInfoResult.Unavailable(Unreadable),
            Answer.InvalidToken => new ClientInfoResult.InvalidToken(),
            Answer.Unavailable unavailable => new ClientInfoResult.Unavailable(unavailable.Reason),
            _ => throw new UnreachableException(),
        };

    public async Task<StatementResult> GetStatementAsync(
        string token, string accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var path = $"personal/statement/{Uri.EscapeDataString(accountId)}/{from.ToUnixTimeSeconds()}/{to.ToUnixTimeSeconds()}";
        return await GetAsync(path, token, "statement", cancellationToken) switch
        {
            Answer.Body body => Read(body.Text, "statement", ParseStatement)
                ?? new StatementResult.Unavailable(Unreadable),
            Answer.InvalidToken => new StatementResult.InvalidToken(),
            Answer.Unavailable unavailable => new StatementResult.Unavailable(unavailable.Reason),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<Answer> GetAsync(string path, string token, string method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Token", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "monobank {Method} request failed.", method);
            return new Answer.Unavailable("monobank did not answer.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("monobank {Method} did not answer within {Timeout}.", method, http.Timeout);
            return new Answer.Unavailable("monobank did not answer in time.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new Answer.InvalidToken();
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("monobank {Method} answered {StatusCode}.", method, (int)response.StatusCode);
                return new Answer.Unavailable("monobank answered with an unexpected status.");
            }

            return new Answer.Body(await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    // Never the exception's own message in the result: it can quote the response body back, and an
    // Unavailable reason flows straight into a ProblemDetails response. The exception (never the token,
    // which this code path never sees) is logged instead.
    private T? Read<T>(string body, string method, Func<string, T> parse)
        where T : class
    {
        try
        {
            return parse(body);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            logger.LogWarning(exception, "monobank {Method} response could not be parsed.", method);
            return null;
        }
    }

    private static ClientInfoResult ParseClientInfo(string body)
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

    private static StatementResult ParseStatement(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"monobank statement root is {root.ValueKind}, not an array.");
        }

        var items = new List<MonobankStatementItem>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"monobank statement item is {item.ValueKind}, not an object.");
            }

            var id = ReadString(item, "id");
            if (string.IsNullOrEmpty(id))
            {
                throw new FormatException("monobank statement item has no id.");
            }

            // GetInt64, GetInt32 and GetBoolean throw on the wrong JSON kind or an out-of-range number,
            // and Read turns that into an unreadable answer rather than a guessed value.
            items.Add(new MonobankStatementItem(
                id,
                DateTimeOffset.FromUnixTimeSeconds(Required(item, "time").GetInt64()),
                ReadString(item, "description"),
                Required(item, "amount").GetInt64(),
                Required(item, "currencyCode").GetInt32(),
                Required(item, "hold").GetBoolean(),
                ReadString(item, "comment"),
                ReadString(item, "counterName")));
        }

        return new StatementResult.Found(items);
    }

    private static JsonElement Required(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value
            : throw new FormatException($"monobank statement item has no \"{property}\".");

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
            throw new FormatException($"monobank \"{property}\" is {value.ValueKind}, not a string.");
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

    private abstract record Answer
    {
        private Answer() { }

        public sealed record Body(string Text) : Answer;

        public sealed record InvalidToken : Answer;

        public sealed record Unavailable(string Reason) : Answer;
    }
}
