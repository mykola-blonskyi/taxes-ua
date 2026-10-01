using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Notifications;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

// How the channel's state and Telegram's answers decide whether a reminder is claimed, kept or retried.
public sealed partial class ReminderTests
{
    [Fact]
    public async Task A_switched_off_channel_gets_nothing_until_it_is_switched_on_again_within_the_window()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await Toggle(owner, false);

        await Run(application);
        Assert.Empty(Texts(telegram));
        Assert.Empty(await SentLog(application));

        await Toggle(owner, true);
        clock.Advance(TimeSpan.FromHours(5));
        await Run(application);
        Assert.Equal([WeekBeforeText], Texts(telegram));
    }

    [Fact]
    public async Task A_blocked_bot_switches_the_channel_off_and_the_reminder_is_not_sent_again_when_it_is_switched_back_on()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user");

        await Run(application);

        Assert.Single(telegram.To("sendMessage"));
        var channel = await Channel(owner);
        Assert.False(channel["enabled"]!.GetValue<bool>());
        Assert.Equal("Blocked", channel["lastFailure"]!.GetValue<string>());
        Assert.Null(Assert.Single(await SentLog(application)).DeliveredAt);

        telegram.SendAnswer = _ => StubTelegramHandler.Ok(new JsonObject { ["message_id"] = 1 });
        await Toggle(owner, true);
        clock.Advance(TimeSpan.FromHours(1));
        await Run(application);
        Assert.Single(telegram.To("sendMessage"));
    }

    [Fact]
    public async Task A_failure_that_may_pass_gives_the_claim_back_so_a_later_run_delivers()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        await Prepare(application, telegram);
        telegram.SendAnswer = _ => StubTelegramHandler.Error(HttpStatusCode.InternalServerError, "Internal Server Error");

        await RunThroughBackoff(application, clock);

        Assert.Equal(1 + TelegramDelivery.Backoff.Length, telegram.To("sendMessage").Count);
        Assert.Empty(await SentLog(application));

        telegram.ClearCalls();
        telegram.SendAnswer = _ => StubTelegramHandler.Ok(new JsonObject { ["message_id"] = 1 });
        clock.Advance(ReminderWorker.Interval);
        await Run(application);
        Assert.Equal([WeekBeforeText], Texts(telegram));
        Assert.NotNull(Assert.Single(await SentLog(application)).DeliveredAt);
    }

    [Fact]
    public async Task The_running_service_sends_a_reminder_when_its_time_comes()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 8, 50));
        await using var application = fixture.CreateApplication(
            telegram,
            clock,
            runPoller: true,
            configure: builder => builder.ConfigureTestServices(services =>
                services.Remove(services.Single(service =>
                    service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(TelegramPollWorker)))));
        await Prepare(application, telegram);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (telegram.To("sendMessage").Count == 0 && DateTime.UtcNow < deadline)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(50);
        }

        Assert.Equal([WeekBeforeText], Texts(telegram));
        Assert.True(telegram.To("sendMessage")[0].At >= Kyiv(TaxDue.AddDays(-7), 9, 0));
        clock.Advance(3 * ReminderWorker.Interval);
        await Task.Delay(200);
        Assert.Single(telegram.To("sendMessage"));
    }

    [Fact]
    public async Task Without_a_bot_token_a_run_sends_nothing_and_claims_nothing()
    {
        var telegram = new StubTelegramHandler();
        var moment = Kyiv(TaxDue.AddDays(-7), 9, 0);
        await using (var linked = fixture.CreateApplication(telegram, new FakeTimeProvider(moment)))
        {
            await Prepare(linked, telegram);
        }

        await using var application = fixture.CreateApplication(
            telegram, new FakeTimeProvider(moment), configure: builder => builder.UseSetting("Telegram:BotToken", string.Empty));
        await Run(application);

        Assert.Empty(telegram.Calls);
        Assert.Empty(await SentLog(application));
    }
}
