using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Tests.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Monobank;

// A sync that stops is told to the owner once per incident (Rule 17). The sender is driven by hand, so
// each pass is one the test names; the real worker is off, the sync workers are on.
public sealed partial class MonobankSyncTests
{
    private const string StaleText =
        "Monobank: синхронізація не працює, остання успішна {0}.\n"
        + "Доходи в застосунку можуть бути неповними. Перевірте підключення monobank.";

    private const string RejectedText =
        "Monobank відхилив токен, синхронізацію зупинено.\n"
        + "Доходи в застосунку не оновлюються. Створіть новий токен і збережіть його в налаштуваннях.";

    [Fact]
    public async Task A_stale_sync_alerts_once_recovery_clears_it_and_a_second_incident_alerts_again()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-stale", ("alert-stale-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2061, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-alert-stale");
        await LinkTelegram(app, owner, telegram);

        await SendAlerts(app);
        Assert.Empty(Alerts(telegram));
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(4));
        await SendAlerts(app);
        await SendAlerts(app);

        var health = (await Health(owner))!;
        Assert.Equal("Stale", health["state"]!.GetValue<string>());
        Assert.NotNull(health["lastSyncedAt"]);
        Assert.Equal([string.Format(StaleText, "01.03.2061")], Alerts(telegram));
        var firstClaim = Assert.Single(await Incidents(app));
        Assert.StartsWith("SyncStale:", firstClaim.Incident, StringComparison.Ordinal);
        Assert.NotNull(firstClaim.DeliveredAt);

        bank.StatementsFail = false;
        await Sync(app, owner);
        await SendAlerts(app);
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Single(Alerts(telegram));

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(4));
        await SendAlerts(app);

        Assert.Equal([string.Format(StaleText, "01.03.2061"), string.Format(StaleText, "05.03.2061")], Alerts(telegram));
        Assert.Equal(2, (await Incidents(app)).Select(claim => claim.Incident).Distinct().Count());
    }

    [Fact]
    public async Task A_sync_a_day_late_raises_no_alert()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-late", ("alert-late-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2062, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-alert-late");
        await LinkTelegram(app, owner, telegram);

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromHours(30));
        await SendAlerts(app);

        Assert.Empty(Alerts(telegram));
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_rejected_token_alerts_once_and_a_new_rejection_after_replacing_it_alerts_again()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-revoked", ("alert-revoked-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2063, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-alert-revoked");
        await LinkTelegram(app, owner, telegram);

        bank.Revoke("token-alert-revoked");
        Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync("/api/monobank/sync", null)).StatusCode);
        await Drain(app, owner);
        await SendAlerts(app);
        await SendAlerts(app);

        Assert.Equal([RejectedText], Alerts(telegram));
        Assert.Equal("TokenRejected", (await Health(owner))!["state"]!.GetValue<string>());

        bank.Connect("token-alert-renewed", ("alert-revoked-uah", 980));
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-alert-renewed" })).StatusCode);
        await Drain(app, owner);
        await SendAlerts(app);
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Single(Alerts(telegram));

        app.Clock.Advance(TimeSpan.FromHours(2));
        bank.Revoke("token-alert-renewed");
        Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync("/api/monobank/sync", null)).StatusCode);
        await Drain(app, owner);
        await SendAlerts(app);

        Assert.Equal([RejectedText, RejectedText], Alerts(telegram));
        Assert.Equal(2, (await Incidents(app)).Select(claim => claim.Incident).Distinct().Count());
    }

    [Fact]
    public async Task An_unreadable_token_is_shown_and_alerted_once()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-unreadable", ("alert-unreadable-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2064, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-alert-unreadable");
        await LinkTelegram(app, owner, telegram);

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.BankAccounts
                .Where(row => row.ExternalId == "alert-unreadable-uah")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LastFailure, SyncFailure.TokenUnreadable)
                    .SetProperty(row => row.LastFailedAt, app.Clock.GetUtcNow()));
        }

        await SendAlerts(app);
        await SendAlerts(app);

        Assert.Equal("TokenUnreadable", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Equal(
            ["Токен monobank не вдалося прочитати, синхронізацію зупинено.\n"
                + "Імовірно, втрачено ключ шифрування. Збережіть токен у налаштуваннях заново."],
            Alerts(telegram));
    }

    [Fact]
    public async Task No_alert_is_claimed_or_sent_without_a_channel_and_the_dashboard_still_says_so()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-nochannel", ("alert-nochannel-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2065, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-alert-nochannel");
        await TelegramSteps.Reset(app.Factory);
        await ClearClaims(app);

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(4));
        await SendAlerts(app);

        Assert.Empty(telegram.To("sendMessage"));
        Assert.Empty(await Incidents(app));
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_dashboard_has_no_sync_health_alert_without_a_connection()
    {
        await using var app = Create(At(2066, 3, 1, 10), new FakeBank());
        using var owner = await ApiFixture.SignIn(app.Factory, ApiFixture.SecondAllowedEmail);
        await owner.DeleteAsync("/api/monobank/connection");

        Assert.Null(await Health(owner));
    }

    // The sync workers run; the reminder worker and the Telegram poller do not, so the test decides when
    // alerts are sent and when the bot is polled.
    private SyncApp CreateAlerting(DateTimeOffset now, FakeBank bank, StubTelegramHandler telegram)
    {
        var clock = new FakeTimeProvider(now);
        telegram.Clock = clock;
        var handler = new StubMonobankHandler(bank.Respond, clock);
        var factory = fixture.CreateApplication(builder => builder
            .UseSetting("Monobank:PublicBaseUrl", string.Empty)
            .UseSetting("Telegram:BotToken", ApiFixture.TelegramTestToken)
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<NbuRateClient>().ConfigurePrimaryHttpMessageHandler(() => Nbu());
                services.AddHttpClient<TelegramClient>().ConfigurePrimaryHttpMessageHandler(() => telegram);
                foreach (var worker in services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType is { } type
                        && (type == typeof(ReminderWorker) || type == typeof(TelegramPollWorker)))
                    .ToArray())
                {
                    services.Remove(worker);
                }
            }));
        return new SyncApp(factory, clock, handler);
    }

    private static async Task LinkTelegram(SyncApp app, HttpClient owner, StubTelegramHandler telegram)
    {
        await TelegramSteps.Reset(app.Factory);
        await ClearClaims(app);
        await TelegramSteps.Link(app.Factory, owner, telegram);
        telegram.ClearCalls();
    }

    private static Task SendAlerts(SyncApp app) =>
        app.Factory.Services.GetRequiredService<ReminderSender>().RunOnceAsync(CancellationToken.None);

    private static List<string> Alerts(StubTelegramHandler telegram) =>
        [.. telegram.To("sendMessage").Select(call => call.Body["text"]!.GetValue<string>())];

    private static async Task<JsonObject?> Health(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonObject>("/api/dashboard"))!["sync"]?.AsObject();

    private static async Task<List<SentReminder>> Incidents(SyncApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id);
        return await database.SentReminders.AsNoTracking()
            .Where(row => owner.Contains(row.UserId) && row.Incident != string.Empty)
            .ToListAsync();
    }

    private static async Task ClearClaims(SyncApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id);
        await database.SentReminders.Where(row => owner.Contains(row.UserId)).ExecuteDeleteAsync();
    }
}
