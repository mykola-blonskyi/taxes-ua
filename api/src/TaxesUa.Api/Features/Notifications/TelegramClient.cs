using System.Net;
using System.Text;
using System.Text.Json;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Notifications;

internal sealed record TelegramIdentity(long Id, string Username);

// The parts of an incoming message the bot acts on. Language is what the sender's client reports,
// used only to answer a chat that is not linked to any owner.
internal sealed record TelegramMessage(long ChatId, bool IsPrivate, string? Text, string? Language);

internal sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message);

// A closed set of outcomes rather than exceptions, like MonobankClient: the ordinary "blocked" and
// "slow down" answers are data the delivery code branches on.
internal readonly record struct TelegramResult<T>(T? Value, DeliveryFailure? Failure, TimeSpan? RetryAfter, HttpStatusCode? Status = null)
{
    public bool IsOk => Failure is null;

    public static TelegramResult<T> Ok(T value) => new(value, null, null);

    public static TelegramResult<T> Fail(DeliveryFailure failure, TimeSpan? retryAfter = null, HttpStatusCode? status = null) =>
        new(default, failure, retryAfter, status);
}

/// <summary>
/// The Bot API's getMe, getUpdates, deleteWebhook and sendMessage, registered as a typed HttpClient like
/// MonobankClient so tests replace its primary handler. The token is part of every request path, so
/// this class never logs a URL or an exception message, and the client is registered without the
/// framework's request logging, which prints the URL (Program.cs).
/// </summary>
internal sealed class TelegramClient(HttpClient http, TelegramBot bot, ILogger<TelegramClient> logger)
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

    public async Task<TelegramResult<TelegramIdentity>> GetMeAsync(CancellationToken cancellationToken)
    {
        var answer = await CallAsync("getMe", new { }, CallTimeout, cancellationToken);
        return answer.Read(result =>
        {
            var username = result.TryGetProperty("username", out var name) ? name.GetString() : null;
            return string.IsNullOrEmpty(username) ? null : new TelegramIdentity(result.GetProperty("id").GetInt64(), username);
        }, logger, "getMe");
    }

    public async Task<TelegramResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(
        long offset, TimeSpan longPoll, CancellationToken cancellationToken)
    {
        var body = new { offset, timeout = (int)longPoll.TotalSeconds, allowed_updates = new[] { "message" } };
        var answer = await CallAsync("getUpdates", body, longPoll + CallTimeout, cancellationToken);
        return answer.Read<IReadOnlyList<TelegramUpdate>>(
            result => [.. result.EnumerateArray().Select(ParseUpdate)], logger, "getUpdates");
    }

    public async Task<TelegramResult<bool>> DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        var answer = await CallAsync("deleteWebhook", new { }, CallTimeout, cancellationToken);
        return answer.Read(_ => true, logger, "deleteWebhook");
    }

    public async Task<TelegramResult<bool>> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var answer = await CallAsync("sendMessage", new { chat_id = chatId, text }, CallTimeout, cancellationToken);
        return answer.Read(_ => true, logger, "sendMessage");
    }

    private async Task<Answer> CallAsync(string method, object payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The leading "./" matters: without it the colon in the token makes "bot123456:..." parse as a
        // URI scheme instead of a path.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"./bot{bot.Token}/{method}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(request, deadline.Token);
            var text = await response.Content.ReadAsStringAsync(deadline.Token);
            return new Answer(response.StatusCode, text);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Telegram {Method} request failed: {Reason}.", method, exception.HttpRequestError);
            return new Answer(null, null, DeliveryFailure.Unreachable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Telegram {Method} did not answer within {Timeout}.", method, timeout);
            return new Answer(null, null, DeliveryFailure.Timeout);
        }
    }

    private static TelegramUpdate ParseUpdate(JsonElement update)
    {
        var id = update.GetProperty("update_id").GetInt64();
        if (!update.TryGetProperty("message", out var message) || !message.TryGetProperty("chat", out var chat))
        {
            return new TelegramUpdate(id, null);
        }

        var text = message.TryGetProperty("text", out var textProperty) ? textProperty.GetString() : null;
        var language = message.TryGetProperty("from", out var from) && from.TryGetProperty("language_code", out var code)
            ? code.GetString()
            : null;
        var isPrivate = chat.TryGetProperty("type", out var type) && type.GetString() == "private";

        return new TelegramUpdate(id, new TelegramMessage(chat.GetProperty("id").GetInt64(), isPrivate, text, language));
    }

    // Never the response body in a log or a result: an error description can quote what was sent.
    private sealed record Answer(HttpStatusCode? Status, string? Body, DeliveryFailure? Transport = null)
    {
        public TelegramResult<T> Read<T>(Func<JsonElement, T?> parse, ILogger logger, string method)
        {
            if (Transport is { } transport)
            {
                return TelegramResult<T>.Fail(transport);
            }

            try
            {
                using var document = JsonDocument.Parse(Body!);
                var root = document.RootElement;
                if (Status == HttpStatusCode.OK && root.GetProperty("ok").GetBoolean())
                {
                    return parse(root.GetProperty("result")) is { } value
                        ? TelegramResult<T>.Ok(value)
                        : TelegramResult<T>.Fail(DeliveryFailure.Unreadable);
                }

                return Classify<T>(root);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                logger.LogWarning("Telegram {Method} answered {Status} with a body that could not be read.", method, (int?)Status);
                return TelegramResult<T>.Fail(Status is >= HttpStatusCode.InternalServerError
                    ? DeliveryFailure.ServerError
                    : DeliveryFailure.Unreadable);
            }
        }

        private TelegramResult<T> Classify<T>(JsonElement root)
        {
            switch (Status)
            {
                case HttpStatusCode.Forbidden:
                    return TelegramResult<T>.Fail(DeliveryFailure.Blocked);
                case HttpStatusCode.TooManyRequests:
                    var seconds = root.TryGetProperty("parameters", out var parameters)
                        && parameters.TryGetProperty("retry_after", out var retryAfter)
                        && retryAfter.TryGetInt32(out var value) ? value : (int?)null;
                    return TelegramResult<T>.Fail(
                        DeliveryFailure.RateLimited, seconds is { } wait ? TimeSpan.FromSeconds(wait) : null);
                case >= HttpStatusCode.InternalServerError:
                    return TelegramResult<T>.Fail(DeliveryFailure.ServerError);
                default:
                    return TelegramResult<T>.Fail(DeliveryFailure.Rejected, status: Status);
            }
        }
    }
}
