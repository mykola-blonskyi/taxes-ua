using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Notifications;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class TelegramChannelTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset Start = new(2031, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Connecting_links_the_chat_that_presses_start_and_confirms_in_the_owners_language()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);

        var before = await Channel(owner);
        Assert.True(before["available"]!.GetValue<bool>());
        Assert.False(before["linked"]!.GetValue<bool>());

        var url = await Connect(owner);
        Assert.StartsWith("https://t.me/test_reminder_bot?start=", url);
        Assert.Equal(32, CodeOf(url).Length);
        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {CodeOf(url)}"));
        await Poll(application);

        var linked = await Channel(owner);
        Assert.True(linked["linked"]!.GetValue<bool>());
        Assert.True(linked["enabled"]!.GetValue<bool>());
        Assert.Equal(Start, linked["linkedAt"]!.GetValue<DateTimeOffset>());
        Assert.Null(linked["lastDeliveryAt"]);
        Assert.Null(linked["lastFailure"]);
        var reply = Assert.Single(telegram.To("sendMessage"));
        Assert.Equal(OwnerChat.ToString(), reply.Body["chat_id"]!.GetValue<string>());
        Assert.Equal(TelegramTexts.Linked("uk"), reply.Body["text"]!.GetValue<string>());
        Assert.DoesNotContain(OwnerChat.ToString(), await owner.GetStringAsync(Channels));
    }

    [Fact]
    public async Task The_confirmation_is_in_russian_when_the_owner_reads_the_app_in_russian()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);
        await SetLocale(owner, "ru");

        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {CodeOf(await Connect(owner))}", language: "uk"));
        await Poll(application);

        Assert.Equal(TelegramTexts.Linked("ru"), Assert.Single(telegram.To("sendMessage")).Body["text"]!.GetValue<string>());
        await SetLocale(owner, "uk");
    }

    [Fact]
    public async Task An_expired_code_links_nothing_and_says_so()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);

        var code = CodeOf(await Connect(owner));
        clock.Advance(TelegramLinking.Lifetime + TimeSpan.FromSeconds(1));
        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {code}"));
        await Poll(application);

        Assert.False((await Channel(owner))["linked"]!.GetValue<bool>());
        Assert.Equal(TelegramTexts.CodeRejected("uk"), Assert.Single(telegram.To("sendMessage")).Body["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);

        var code = CodeOf(await Connect(owner));
        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {code}"));
        telegram.Updates.Add(StubTelegramHandler.Update(11, 9999, $"/start {code}"));
        await Poll(application);

        var replies = telegram.To("sendMessage");
        Assert.Equal(2, replies.Count);
        Assert.Equal(TelegramTexts.Linked("uk"), replies[0].Body["text"]!.GetValue<string>());
        Assert.Equal("9999", replies[1].Body["chat_id"]!.GetValue<string>());
        Assert.Equal(TelegramTexts.CodeRejected("uk"), replies[1].Body["text"]!.GetValue<string>());

        await Test(owner);
        Assert.Equal(OwnerChat.ToString(), telegram.To("sendMessage")[^1].Body["chat_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_new_code_replaces_the_one_before_it()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);

        var first = CodeOf(await Connect(owner));
        var second = CodeOf(await Connect(owner));
        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {first}"));
        await Poll(application);

        Assert.NotEqual(first, second);
        Assert.False((await Channel(owner))["linked"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Anything_else_gets_a_short_reply_and_a_group_is_ignored()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);

        var code = CodeOf(await Connect(owner));
        telegram.Updates.Add(StubTelegramHandler.Update(10, 7001, "hello", language: "ru"));
        telegram.Updates.Add(StubTelegramHandler.Update(11, 7002, "/start"));
        telegram.Updates.Add(StubTelegramHandler.Update(12, 7003, null));
        telegram.Updates.Add(StubTelegramHandler.Update(13, -100500, $"/start {code}", chatType: "supergroup"));
        await Poll(application);

        var replies = telegram.To("sendMessage");
        Assert.Equal(["7001", "7002", "7003"], replies.Select(reply => reply.Body["chat_id"]!.GetValue<string>()));
        Assert.Equal(TelegramTexts.NotUnderstood("ru"), replies[0].Body["text"]!.GetValue<string>());
        Assert.Equal(TelegramTexts.NotUnderstood("uk"), replies[1].Body["text"]!.GetValue<string>());
        Assert.False((await Channel(owner))["linked"]!.GetValue<bool>());

        telegram.Updates.Add(StubTelegramHandler.Update(14, OwnerChat, $"/start@test_reminder_bot {code}"));
        await Poll(application);
        Assert.True((await Channel(owner))["linked"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_poll_offset_survives_a_restart()
    {
        var telegram = new StubTelegramHandler();
        await using (var first = fixture.CreateApplication(telegram, new FakeTimeProvider(Start)))
        {
            using var owner = await SignIn(first);
            telegram.Updates.Add(StubTelegramHandler.Update(10, 7001, "one"));
            telegram.Updates.Add(StubTelegramHandler.Update(11, 7001, "two"));
            await Poll(first);
            Assert.Equal(2, telegram.To("sendMessage").Count);
        }

        var afterRestart = new StubTelegramHandler();
        afterRestart.Updates.AddRange(telegram.Updates.Select(update => (JsonObject)JsonNode.Parse(update.ToJsonString())!));
        await using var second = fixture.CreateApplication(afterRestart, new FakeTimeProvider(Start));
        await Poll(second);

        Assert.Equal(12, Assert.Single(afterRestart.To("getUpdates")).Body["offset"]!.GetValue<long>());
        Assert.Empty(afterRestart.To("sendMessage"));
    }

    [Fact]
    public async Task The_running_service_links_a_chat_without_anyone_calling_the_poller()
    {
        var telegram = new StubTelegramHandler { HoldWhenEmpty = true };
        // Before the service starts: it reads the stored offset once, when its first round begins.
        await using (var scope = fixture.CreateScope())
        {
            await Reset(scope.ServiceProvider);
        }

        await using var application = fixture.CreateApplication(telegram, runPoller: true);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var code = CodeOf(await Connect(owner));
        telegram.Push(StubTelegramHandler.Update(10, OwnerChat, $"/start {code}"));

        JsonObject channel;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            await Task.Delay(50);
            channel = await Channel(owner);
        }
        while (!channel["linked"]!.GetValue<bool>() && DateTime.UtcNow < deadline);

        Assert.True(channel["linked"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task Without_a_usable_token_the_channel_is_unavailable_and_nothing_polls(string token)
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(
            telegram, runPoller: true, configure: builder => builder.UseSetting("Telegram:BotToken", token));
        using var owner = await SignIn(application);

        var channel = await Channel(owner);
        await Task.Delay(300);

        Assert.False(channel["available"]!.GetValue<bool>());
        Assert.False(channel["linked"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.PostAsync(Channels + "/telegram/connect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.PostAsync(Channels + "/telegram/test", null)).StatusCode);
        Assert.Empty(telegram.Calls);
    }

    [Fact]
    public async Task A_test_message_goes_to_the_linked_chat()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        clock.Advance(TimeSpan.FromMinutes(5));

        var channel = await Test(owner);

        var sent = telegram.To("sendMessage")[^1];
        Assert.Equal(OwnerChat.ToString(), sent.Body["chat_id"]!.GetValue<string>());
        Assert.Equal(TelegramTexts.Test("uk"), sent.Body["text"]!.GetValue<string>());
        Assert.Equal(clock.GetUtcNow(), channel["lastDeliveryAt"]!.GetValue<DateTimeOffset>());
        Assert.Null(channel["lastFailure"]);
    }

    [Fact]
    public async Task A_test_message_needs_a_linked_channel()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);
        await Reset(application);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(Channels + "/telegram/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(Channels + "/telegram", new { enabled = true })).StatusCode);
        Assert.Empty(telegram.To("sendMessage"));
    }

    [Fact]
    public async Task A_transient_failure_is_retried_with_backoff_and_then_delivers()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        var linkedCalls = telegram.To("sendMessage").Count;
        telegram.SendAnswer = attempt => attempt - linkedCalls < 3
            ? StubTelegramHandler.Error(HttpStatusCode.InternalServerError, "Internal Server Error")
            : StubTelegramHandler.Ok(new JsonObject());

        var result = await Deliver(application, clock);

        Assert.Equal(DeliveryOutcome.Sent, result.Outcome);
        var attempts = telegram.To("sendMessage").Skip(linkedCalls).ToList();
        Assert.Equal(4, attempts.Count);
        AssertGaps(attempts, TelegramDelivery.Backoff);
        var channel = await Channel(owner);
        Assert.Null(channel["lastFailure"]);
        Assert.NotNull(channel["lastDeliveryAt"]);
    }

    [Fact]
    public async Task Three_retries_are_all_it_takes_before_the_failure_shows_on_the_channel()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => throw new HttpRequestException("connection refused");
        var linkedCalls = telegram.To("sendMessage").Count;

        var result = await Deliver(application, clock);

        Assert.Equal(new DeliveryResult(DeliveryOutcome.Failed, DeliveryFailure.Unreachable), result);
        var attempts = telegram.To("sendMessage").Skip(linkedCalls).ToList();
        Assert.Equal(4, attempts.Count);
        AssertGaps(attempts, TelegramDelivery.Backoff);
        var channel = await Channel(owner);
        Assert.Equal("Unreachable", channel["lastFailure"]!.GetValue<string>());
        Assert.Equal(clock.GetUtcNow(), channel["lastFailureAt"]!.GetValue<DateTimeOffset>());
        Assert.True(channel["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_429_is_waited_out_for_as_long_as_telegram_asks()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        var linkedCalls = telegram.To("sendMessage").Count;
        telegram.SendAnswer = attempt => attempt == linkedCalls
            ? StubTelegramHandler.Error(HttpStatusCode.TooManyRequests, "Too Many Requests: retry after 9", retryAfter: 9)
            : StubTelegramHandler.Ok(new JsonObject());

        var result = await Deliver(application, clock);

        Assert.Equal(DeliveryOutcome.Sent, result.Outcome);
        var attempts = telegram.To("sendMessage").Skip(linkedCalls).ToList();
        Assert.Equal(2, attempts.Count);
        AssertGaps(attempts, [TimeSpan.FromSeconds(9)]);
    }

    [Fact]
    public async Task A_429_asking_for_minutes_is_not_waited_out()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.TooManyRequests, "Too Many Requests", retryAfter: 120);
        var linkedCalls = telegram.To("sendMessage").Count;

        var result = await Deliver(application, clock);

        Assert.Equal(new DeliveryResult(DeliveryOutcome.Failed, DeliveryFailure.RateLimited), result);
        Assert.Single(telegram.To("sendMessage").Skip(linkedCalls));
        Assert.Equal("RateLimited", (await Channel(owner))["lastFailure"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_403_means_the_bot_was_blocked_so_the_channel_switches_off_without_a_retry()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user");
        var linkedCalls = telegram.To("sendMessage").Count;

        var result = await Deliver(application, clock);

        Assert.Equal(new DeliveryResult(DeliveryOutcome.Failed, DeliveryFailure.Blocked), result);
        Assert.Single(telegram.To("sendMessage").Skip(linkedCalls));
        var channel = await Channel(owner);
        Assert.False(channel["enabled"]!.GetValue<bool>());
        Assert.True(channel["linked"]!.GetValue<bool>());
        Assert.Equal("Blocked", channel["lastFailure"]!.GetValue<string>());

        var skipped = await Deliver(application, clock);
        Assert.Equal(DeliveryOutcome.Disabled, skipped.Outcome);
        Assert.Single(telegram.To("sendMessage").Skip(linkedCalls));
    }

    [Fact]
    public async Task A_request_telegram_rejects_is_not_retried_and_leaves_the_channel_on()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.BadRequest, "Bad Request: chat not found");
        var linkedCalls = telegram.To("sendMessage").Count;

        var result = await Deliver(application, clock);

        Assert.Equal(new DeliveryResult(DeliveryOutcome.Failed, DeliveryFailure.Rejected), result);
        Assert.Single(telegram.To("sendMessage").Skip(linkedCalls));
        Assert.True((await Channel(owner))["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_test_button_reports_a_failure_and_settings_shows_it()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user");

        var response = await owner.PostAsync(Channels + "/telegram/test", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("Blocked", (await Channel(owner))["lastFailure"]!.GetValue<string>());
    }

    [Fact]
    public async Task Switching_on_again_clears_the_failure_and_switching_off_stops_reminders_but_not_the_test()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user");
        await Deliver(application, clock);
        telegram.SendAnswer = _ => StubTelegramHandler.Ok(new JsonObject());

        var on = await Toggle(owner, true);
        Assert.True(on["enabled"]!.GetValue<bool>());
        Assert.Null(on["lastFailure"]);
        Assert.Equal(DeliveryOutcome.Sent, (await Deliver(application, clock)).Outcome);

        var off = await Toggle(owner, false);
        Assert.False(off["enabled"]!.GetValue<bool>());
        var calls = telegram.To("sendMessage").Count;
        Assert.Equal(DeliveryOutcome.Disabled, (await Deliver(application, clock)).Outcome);
        Assert.Equal(calls, telegram.To("sendMessage").Count);
        await Test(owner);
        Assert.Equal(calls + 1, telegram.To("sendMessage").Count);
    }

    [Fact]
    public async Task Disconnecting_removes_the_channel_and_any_code_still_open()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);
        await Link(application, owner, telegram);
        var pending = CodeOf(await Connect(owner));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Channels + "/telegram")).StatusCode);

        Assert.False((await Channel(owner))["linked"]!.GetValue<bool>());
        telegram.Updates.Add(StubTelegramHandler.Update(20, OwnerChat, $"/start {pending}"));
        await Poll(application);
        Assert.False((await Channel(owner))["linked"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Channels + "/telegram")).StatusCode);
    }

    [Fact]
    public async Task Each_owner_sees_and_changes_only_their_own_channel()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Start));
        using var owner = await SignIn(application);
        using var other = await SignIn(application, ApiFixture.SecondAllowedEmail);
        await Link(application, owner, telegram);

        Assert.False((await Channel(other))["linked"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Conflict, (await other.PutAsJsonAsync(Channels + "/telegram", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsync(Channels + "/telegram/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync(Channels + "/telegram")).StatusCode);
        Assert.True((await Channel(owner))["linked"]!.GetValue<bool>());

        telegram.Updates.Add(StubTelegramHandler.Update(30, 5151, $"/start {CodeOf(await Connect(other))}"));
        await Poll(application);
        Assert.True((await Channel(other))["linked"]!.GetValue<bool>());

        await Test(owner);
        await Test(other);
        var chats = telegram.To("sendMessage").TakeLast(2).Select(call => call.Body["chat_id"]!.GetValue<string>());
        Assert.Equal([OwnerChat.ToString(), "5151"], chats);
        await other.DeleteAsync(Channels + "/telegram");
    }

    [Fact]
    public async Task The_change_log_records_linking_and_toggling_but_not_deliveries()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(telegram, clock);
        using var owner = await SignIn(application);
        await Reset(application);
        await Link(application, owner, telegram);
        var afterLinking = await History(owner);

        await Test(owner);
        Assert.Equal(afterLinking.Length, (await History(owner)).Length);

        await Toggle(owner, false);
        var history = await History(owner);
        Assert.Equal(afterLinking.Length + 1, history.Length);
        var created = history.Last(entry => entry["action"]!.GetValue<string>() == "Create");
        Assert.True(created["after"]!["enabled"]!.GetValue<bool>());
        Assert.Null(created["after"]!["lastDeliveryAt"]);
        Assert.Null(created["after"]!["lastFailure"]);
        Assert.False(history[0]["after"]!["enabled"]!.GetValue<bool>());
        Assert.True(history[0]["before"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_token_never_reaches_a_log_or_a_response()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Start);
        var logs = new CapturedLogs();
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddFilter((_, _, _) => true);
                logging.AddProvider(logs);
            }));
        using var owner = await SignIn(application);
        await Reset(application);
        var bodies = new List<string>();

        var connect = await owner.PostAsync(Channels + "/telegram/connect", null);
        bodies.Add(await connect.Content.ReadAsStringAsync());
        telegram.Updates.Add(StubTelegramHandler.Update(10, OwnerChat, $"/start {CodeOf((await connect.Content.ReadFromJsonAsync<JsonObject>())!["url"]!.GetValue<string>())}"));
        await Poll(application);
        bodies.Add(await owner.GetStringAsync(Channels));

        telegram.Override = call => call.Method == "getUpdates" ? throw new HttpRequestException("refused") : null;
        await Poll(application);
        telegram.Override = null;

        telegram.SendAnswer = _ => throw new HttpRequestException("refused");
        var failing = owner.PostAsync(Channels + "/telegram/test", null);
        while (!failing.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        bodies.Add(await (await failing).Content.ReadAsStringAsync());
        bodies.Add(await owner.GetStringAsync(Channels));

        Assert.Contains(logs.Lines, line => line.StartsWith("TaxesUa.Api.Features.Notifications", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ApiFixture.TelegramTestSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("bot123456", StringComparison.Ordinal));
        Assert.All(bodies, body => Assert.DoesNotContain(ApiFixture.TelegramTestSecret, body));
        Assert.All(bodies, body => Assert.DoesNotContain("123456:", body));
        Assert.All(telegram.Calls, call => Assert.StartsWith("/bot" + ApiFixture.TelegramTestToken + "/", call.Path));
    }

    [Fact]
    public async Task The_framework_would_log_the_token_if_the_client_kept_its_request_logging()
    {
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddFilter((_, _, _) => true);
            logging.AddProvider(logs);
        });
        services.AddHttpClient("plain", client => client.BaseAddress = new Uri("https://telegram.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubTelegramHandler());
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IHttpClientFactory>().CreateClient("plain")
            .PostAsync($"bot{ApiFixture.TelegramTestToken}/getMe", new StringContent("{}"));

        Assert.Contains(logs.Lines, line => line.Contains(ApiFixture.TelegramTestSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_registered_client_has_no_request_logging_at_all()
    {
        var logs = new CapturedLogs();
        await using var application = fixture.CreateApplication(
            new StubTelegramHandler(), new FakeTimeProvider(Start), configure: builder => builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddFilter((_, _, _) => true);
                logging.AddProvider(logs);
            }));
        using var owner = await SignIn(application);

        await Connect(owner);

        Assert.DoesNotContain(logs.Lines, line => line.StartsWith("System.Net.Http.HttpClient.TelegramClient", StringComparison.Ordinal));
    }

    private static void AssertGaps(IReadOnlyList<TelegramCall> attempts, IReadOnlyList<TimeSpan> atLeast)
    {
        for (var i = 0; i < atLeast.Count; i++)
        {
            Assert.True(
                attempts[i + 1].At - attempts[i].At >= atLeast[i],
                $"attempt {i + 2} came {attempts[i + 1].At - attempts[i].At} after attempt {i + 1}, before {atLeast[i]}");
        }
    }

    private Task<HttpClient> SignIn(WebApplicationFactory<Program> application) => SignIn(application, ApiFixture.AllowedEmail);

    private async Task<HttpClient> SignIn(WebApplicationFactory<Program> application, string email)
    {
        var client = await ApiFixture.SignIn(application, email);
        await Reset(application, email);
        return client;
    }

    private static async Task<JsonObject> Test(HttpClient client)
    {
        var response = await client.PostAsync(Channels + "/telegram/test", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<DeliveryResult> Deliver(WebApplicationFactory<Program> application, FakeTimeProvider clock)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = await database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id).SingleAsync();
        var delivery = scope.ServiceProvider.GetRequiredService<TelegramDelivery>();

        var sending = delivery.SendAsync(ownerId, "reminder", evenIfDisabled: false, CancellationToken.None);
        while (!sending.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        return await sending;
    }

    private static async Task<JsonObject[]> History(HttpClient owner) =>
        [.. (await owner.GetFromJsonAsync<JsonArray>("/api/audit?entity=NotificationChannel"))!.Select(entry => entry!.AsObject())];
}
