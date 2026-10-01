using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Engine;
using static TaxesUa.Api.Tests.Features.Notifications.EmailChannelTests;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

// The email channel behind the same sender and the same sent log as Telegram: one reminder, one claim
// per channel, the claim's key carrying the channel.
public sealed partial class ReminderTests
{
    private const string MailAddress = "owner@mail.test";

    [Fact]
    public async Task A_reminder_goes_to_a_confirmed_address_once_as_plain_text_and_html()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application);
        await Connected(owner, email, MailAddress);

        await Run(application);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Run(application);

        var sent = Assert.Single(email.Delivered);
        Assert.Equal(MailAddress, sent.To);
        Assert.Equal("Податки: строк 20.05.2031, через 7 днів.", sent.Subject);
        Assert.Equal(WeekBeforeText + "\nВідкрити застосунок: https://taxes.test/", sent.Text);
        Assert.Contains($"<p>{WeekBeforeText.Split('\n')[1]}</p>", sent.Html);
        Assert.Contains("<p>Відкрити застосунок: <a href=\"https://taxes.test/\">https://taxes.test/</a></p>", sent.Html);
        var claim = Assert.Single(await SentLog(application));
        Assert.Equal(NotificationChannelKind.Email, claim.Channel);
        Assert.Equal((TaxDue, ReminderKinds.SingleTax | ReminderKinds.MilitaryLevy, ReminderOffset.WeekBefore), (claim.Date, claim.Kinds, claim.Offset));
        Assert.NotNull(claim.DeliveredAt);
    }

    [Fact]
    public async Task An_address_waiting_for_its_link_receives_the_confirmation_and_no_reminder()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application);
        await Add(owner, MailAddress);

        await Run(application);
        clock.Advance(TimeSpan.FromHours(2));
        await Run(application);

        Assert.Equal("Підтвердьте адресу для нагадувань про податки", Assert.Single(email.Attempts).Message.Subject);
        Assert.Empty(await SentLog(application));

        await Confirm(owner, TokenOf(email.Delivered[0]));
        await Run(application);
        Assert.Equal([MailAddress, MailAddress], email.Delivered.Select(message => message.To));
        Assert.Equal(WeekBeforeText + "\nВідкрити застосунок: https://taxes.test/", email.Delivered[1].Text);
    }

    [Fact]
    public async Task A_switched_off_address_gets_no_reminder()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application);
        await Connected(owner, email, MailAddress);
        await SwitchEmail(owner, false);

        await Run(application);

        Assert.Empty(email.Attempts);
        Assert.Empty(await SentLog(application));
    }

    [Fact]
    public async Task A_reminder_in_russian_goes_in_russian()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application, "ru");
        await Connected(owner, email, MailAddress);

        await Run(application);

        Assert.Equal("Налоги: срок 20.05.2031, через 7 дней.", Assert.Single(email.Delivered).Subject);
        await SetLocale(owner, "uk");
    }

    [Fact]
    public async Task A_failure_that_may_pass_gives_the_claim_back_and_a_later_run_delivers_by_email()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application);
        await Connected(owner, email, MailAddress);
        email.Answer = (_, _) => new DeliveryAttempt(DeliveryFailure.ServerError);

        await RunThroughBackoff(application, clock);

        Assert.Equal(1 + ChannelDelivery.Backoff.Length, email.Attempts.Count);
        Assert.Empty(await SentLog(application));
        Assert.Equal("ServerError", (await Channel(owner, "Email"))["lastFailure"]!.GetValue<string>());

        email.Clear();
        email.Answer = null;
        clock.Advance(ReminderWorker.Interval);
        await Run(application);
        Assert.Equal(WeekBeforeText + "\nВідкрити застосунок: https://taxes.test/", Assert.Single(email.Delivered).Text);
        Assert.NotNull(Assert.Single(await SentLog(application)).DeliveredAt);
        Assert.Null((await Channel(owner, "Email"))["lastFailure"]);
    }

    // A refused sign-in or recipient is final and a timeout may have delivered: none is retried, in the
    // channel or by a later run, and the claim stays.
    [Theory]
    [InlineData("Authentication")]
    [InlineData("Rejected")]
    [InlineData("Timeout")]
    public async Task A_failure_that_will_not_pass_or_may_have_delivered_is_not_retried_and_keeps_the_claim(string name)
    {
        var failure = Enum.Parse<DeliveryFailure>(name);
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(email, clock);
        var owner = await PrepareOwner(application);
        await Connected(owner, email, MailAddress);
        email.Answer = (_, _) => new DeliveryAttempt(failure);

        await Run(application);
        Assert.Single(email.Attempts);
        Assert.Null(Assert.Single(await SentLog(application)).DeliveredAt);

        email.Answer = null;
        clock.Advance(ReminderWorker.Interval);
        await Run(application);

        Assert.Single(email.Attempts);
        Assert.Null(Assert.Single(await SentLog(application)).DeliveredAt);
        Assert.Equal(name, (await Channel(owner, "Email"))["lastFailure"]!.GetValue<string>());
        Assert.True((await Channel(owner, "Email"))["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Without_smtp_settings_a_run_sends_nothing_and_claims_nothing()
    {
        var email = new InMemoryEmailTransport();
        var moment = Kyiv(TaxDue.AddDays(-7), 9, 0);
        await using (var configured = fixture.CreateApplication(email, new FakeTimeProvider(moment)))
        {
            var owner = await PrepareOwner(configured);
            await Connected(owner, email, MailAddress);
        }

        await using var application = fixture.CreateApplication(
            email, new FakeTimeProvider(moment), configure: builder => builder.UseSetting("Smtp:Host", string.Empty));
        await Run(application);

        Assert.Empty(email.Attempts);
        Assert.Empty(await SentLog(application));
    }

    [Fact]
    public async Task Telegram_and_email_each_get_their_own_claim_for_the_same_reminder()
    {
        var telegram = new StubTelegramHandler();
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(
            email, clock, configure: builder =>
            {
                telegram.Clock = clock;
                builder.UseSetting("Telegram:BotToken", ApiFixture.TelegramTestToken);
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient<TelegramClient>().ConfigurePrimaryHttpMessageHandler(() => telegram));
            });
        var owner = await Prepare(application, telegram);
        await Connected(owner, email, MailAddress);

        await Run(application);

        Assert.Single(email.Delivered);
        Assert.Single(telegram.To("sendMessage"));
        Assert.Equal(
            [NotificationChannelKind.Telegram, NotificationChannelKind.Email],
            (await SentLog(application)).Select(claim => claim.Channel).Order());
        Assert.All(await SentLog(application), claim => Assert.NotNull(claim.DeliveredAt));
    }
}
