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
// and negative for a debit. CurrencyCode is kept as the bank sent it, and is not trusted to name the
// currency of Amount.
internal sealed record MonobankStatementItem(
    string Id,
    DateTimeOffset Time,
    string? Description,
    long Amount,
    int CurrencyCode,
    bool Hold,
    string? Comment,
    string? CounterName,
    string? CounterIban,
    string? CounterEdrpou = null);

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

    // 429. RetryAfter is the bank's Retry-After header when it sent one.
    public sealed record RateLimited(TimeSpan? RetryAfter) : StatementResult;

    public sealed record Unavailable(SyncFailure Failure) : StatementResult;
}

internal abstract record WebhookResult
{
    private WebhookResult() { }

    public sealed record Set : WebhookResult;

    public sealed record InvalidToken : WebhookResult;

    public sealed record Unavailable(SyncFailure Failure) : WebhookResult;
}

/// <summary>
/// The monobank personal API's <c>client-info</c>, <c>statement</c> and <c>webhook</c> endpoints, registered as a typed
/// HttpClient like NbuRateClient so tests replace its primary handler instead of reaching the real bank
/// (ADR-011). Answers are parsed into records here; no business rule lives in this class.
/// </summary>
internal sealed class MonobankClient(HttpClient http, TimeProvider time, ILogger<MonobankClient> logger)
{
    // monobank answers at most this many operations per statement call; a full page means older ones remain.
    public const int StatementPageSize = 500;

    public async Task<ClientInfoResult> GetClientInfoAsync(string token, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Get, "personal/client-info", token, "client-info", null, cancellationToken) switch
        {
            Answer.Body body => Read(body.Text, "client-info", ParseClientInfo)
                ?? new ClientInfoResult.Unavailable(Describe(SyncFailure.UnreadableAnswer)),
            Answer.InvalidToken => new ClientInfoResult.InvalidToken(),
            Answer.RateLimited => new ClientInfoResult.Unavailable(Describe(SyncFailure.RateLimited)),
            Answer.Unavailable unavailable => new ClientInfoResult.Unavailable(Describe(unavailable.Failure)),
            _ => throw new UnreachableException(),
        };

    public async Task<StatementResult> GetStatementAsync(
        string token, string accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var path = $"personal/statement/{Uri.EscapeDataString(accountId)}/{from.ToUnixTimeSeconds()}/{to.ToUnixTimeSeconds()}";
        return await SendAsync(HttpMethod.Get, path, token, "statement", null, cancellationToken) switch
        {
            Answer.Body body => Read(body.Text, "statement", ParseStatement)
                ?? new StatementResult.Unavailable(SyncFailure.UnreadableAnswer),
            Answer.InvalidToken => new StatementResult.InvalidToken(),
            Answer.RateLimited limited => new StatementResult.RateLimited(limited.RetryAfter),
            Answer.Unavailable unavailable => new StatementResult.Unavailable(unavailable.Failure),
            _ => throw new UnreachableException(),
        };
    }

    // monobank checks a non-empty URL with a GET that must answer 200 before it answers here, and an
    // empty URL removes the webhook.
    public async Task<WebhookResult> SetWebhookAsync(string token, string url, CancellationToken cancellationToken)
    {
        // Serialized up front so the request carries a Content-Length: JsonContent streams chunked.
        var content = new StringContent(
            JsonSerializer.Serialize(new { webHookUrl = url }), System.Text.Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, "personal/webhook", token, "webhook", content, cancellationToken) switch
        {
            Answer.Body => new WebhookResult.Set(),
            Answer.InvalidToken => new WebhookResult.InvalidToken(),
            Answer.RateLimited => new WebhookResult.Unavailable(SyncFailure.RateLimited),
            Answer.Unavailable unavailable => new WebhookResult.Unavailable(unavailable.Failure),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<Answer> SendAsync(
        HttpMethod httpMethod, string path, string token, string method, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(httpMethod, path) { Content = content };
        request.Headers.Add("X-Token", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "monobank {Method} request failed.", method);
            return new Answer.Unavailable(SyncFailure.BankUnreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("monobank {Method} did not answer within {Timeout}.", method, http.Timeout);
            return new Answer.Unavailable(SyncFailure.BankTimeout);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new Answer.InvalidToken();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new Answer.RateLimited(RetryAfter(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("monobank {Method} answered {StatusCode}.", method, (int)response.StatusCode);
                return new Answer.Unavailable(SyncFailure.BankError);
            }

            return new Answer.Body(await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - time.GetUtcNow(),
        _ => null,
    };

    private static string Describe(SyncFailure failure) => failure switch
    {
        SyncFailure.BankUnreachable => "monobank did not answer.",
        SyncFailure.BankTimeout => "monobank did not answer in time.",
        SyncFailure.RateLimited => "monobank asked to wait before the next request.",
        SyncFailure.UnreadableAnswer => "monobank's response could not be read.",
        _ => "monobank answered with an unexpected status.",
    };

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
                ReadString(item, "counterName"),
                ReadString(item, "counterIban"),
                ReadString(item, "counterEdrpou")));
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

        public sealed record RateLimited(TimeSpan? RetryAfter) : Answer;

        public sealed record Unavailable(SyncFailure Failure) : Answer;
    }
}
