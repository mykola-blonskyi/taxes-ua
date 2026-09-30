using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed record TelegramCall(string Path, string Method, JsonObject Body, DateTimeOffset At);

// A Bot API that behaves the way the real one does where the app depends on it: getUpdates returns the
// updates from the requested offset on, and getMe names the bot. Everything else is scripted per test.
public sealed class StubTelegramHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<TelegramCall> _calls = new();

    private readonly SemaphoreSlim _pushed = new(0);

    private int _sendAttempts;

    // The clock the calls are stamped with; the fixture sets it to the application's fake clock.
    public TimeProvider? Clock { get; set; }

    public string Username { get; set; } = "test_reminder_bot";

    // Raw update objects, each with its update_id.
    public List<JsonObject> Updates { get; } = [];

    // Long polling: with nothing to return, wait for the caller to give up, as Telegram does for 30 s.
    public bool HoldWhenEmpty { get; set; }

    // The answer to sendMessage by attempt number, counting from 0 across the handler's life.
    public Func<int, HttpResponseMessage> SendAnswer { get; set; } = _ => Ok(new JsonObject { ["message_id"] = 1 });

    public Func<TelegramCall, HttpResponseMessage?>? Override { get; set; }

    // An update arriving while a long poll is being held wakes it.
    public void Push(JsonObject update)
    {
        lock (Updates)
        {
            Updates.Add(update);
        }

        _pushed.Release();
    }

    public IReadOnlyCollection<TelegramCall> Calls => _calls;

    public IReadOnlyList<TelegramCall> To(string method) => [.. _calls.Where(call => call.Method == method)];

    public static HttpResponseMessage Ok(JsonNode result) => Json(
        HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = result });

    public static HttpResponseMessage Error(HttpStatusCode status, string description, int? retryAfter = null)
    {
        var body = new JsonObject { ["ok"] = false, ["error_code"] = (int)status, ["description"] = description };
        if (retryAfter is { } seconds)
        {
            body["parameters"] = new JsonObject { ["retry_after"] = seconds };
        }

        return Json(status, body);
    }

    public static JsonObject Update(long id, long chatId, string? text, string language = "uk", string chatType = "private") => new()
    {
        ["update_id"] = id,
        ["message"] = new JsonObject
        {
            ["message_id"] = id,
            ["from"] = new JsonObject { ["id"] = chatId, ["language_code"] = language },
            ["chat"] = new JsonObject { ["id"] = chatId, ["type"] = chatType },
            ["text"] = text,
        },
    };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var text = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var call = new TelegramCall(path, path[(path.LastIndexOf('/') + 1)..], JsonNode.Parse(text)!.AsObject(), (Clock ?? TimeProvider.System).GetUtcNow());
        _calls.Enqueue(call);

        if (Override?.Invoke(call) is { } overridden)
        {
            return overridden;
        }

        switch (call.Method)
        {
            case "getMe":
                return Ok(new JsonObject { ["id"] = 123456, ["is_bot"] = true, ["username"] = Username });
            case "getUpdates":
                var offset = call.Body["offset"]!.GetValue<long>();
                while (true)
                {
                    var due = new JsonArray();
                    lock (Updates)
                    {
                        foreach (var update in Updates.Where(update => update["update_id"]!.GetValue<long>() >= offset))
                        {
                            due.Add(JsonNode.Parse(update.ToJsonString()));
                        }
                    }

                    if (due.Count > 0 || !HoldWhenEmpty)
                    {
                        return Ok(due);
                    }

                    await _pushed.WaitAsync(cancellationToken);
                }
            case "sendMessage":
                return SendAnswer(Interlocked.Increment(ref _sendAttempts) - 1);
            default:
                return Error(HttpStatusCode.NotFound, "Not Found");
        }
    }
}
