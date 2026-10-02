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
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Monobank;

// A sync that stops is told to the owner once per incident (Rule 17). The sender is driven by hand, so
// each pass is one the test names; the real worker is off, the sync worker is on.
public sealed partial class MonobankSyncTests
{
    private const string StaleText =
        "Monobank: синхронізація не працює, оновлень немає з {0}.\n"
        + "Доходи в застосунку можуть бути неповними. Перевірте підключення monobank.";

    private const string RejectedText =
        "Monobank відхилив токен, синхронізацію зупинено.\n"
        + "Доходи в застосунку не оновлюються. Створіть новий токен і збережіть його в налаштуваннях.";

    // The class shares one database, and every application's sender walks every owner in it, so an earlier
    // test's owner (a linked chat, a rejected token) is alerted through this test's stub too. Each test
    // links a chat of its own and reads only what went there.
    private readonly long _chat = Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);

    [Fact]
    public async Task A_stale_sync_alerts_once_recovery_clears_it_and_a_second_incident_alerts_again()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-stale", ("alert-stale-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2061, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-stale");
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
    public async Task A_backfill_that_stalls_is_stale_and_alerts_once()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-stall", ("alert-stall-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2071, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-stall");
        await LinkTelegram(app, owner, telegram);
        var begun = app.Clock.GetUtcNow();
        await Backfill(app, "alert-stall-uah");

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Empty(Alerts(telegram));
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());

        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        await SendAlerts(app);

        var health = (await Health(owner))!;
        Assert.Equal("Stale", health["state"]!.GetValue<string>());
        Assert.Equal(begun, health["lastSyncedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal([string.Format(StaleText, "01.03.2071")], Alerts(telegram));
        Assert.StartsWith("SyncStale:", Assert.Single(await Incidents(app)).Incident, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backfill_that_keeps_making_progress_never_alerts()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-progress", ("alert-progress-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2072, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-progress");
        await LinkTelegram(app, owner, telegram);
        await Backfill(app, "alert-progress-uah");

        bank.StatementsFail = true;
        for (var step = 0; step < 4; step++)
        {
            app.Clock.Advance(TimeSpan.FromDays(2));
            await AddBatch(app, "alert-progress-uah");
            await SendAlerts(app);
        }

        // Eight days since the first window, two since the latest.
        Assert.Empty(Alerts(telegram));
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Empty(await Incidents(app));

        app.Clock.Advance(TimeSpan.FromDays(4));
        await SendAlerts(app);
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Single(Alerts(telegram));
    }

    [Fact]
    public async Task A_restored_backfill_is_not_stale_until_three_days_after_the_restore()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-restore", ("alert-restore-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2074, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-restore");
        await LinkTelegram(app, owner, telegram);
        var backup = await owner.GetStringAsync("/api/backup");

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(10));
        var restored = await owner.PostAsync("/api/restore", new StringContent(backup, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await Drain(app, owner);

        await SendAlerts(app);
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());
        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Empty(Alerts(telegram));

        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Equal([string.Format(StaleText, "11.03.2074")], Alerts(telegram));
    }

    [Fact]
    public async Task A_backfill_followed_again_after_ten_days_is_not_stale_until_three_days_later()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-refollow", ("alert-refollow-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2075, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-refollow");
        await LinkTelegram(app, owner, telegram);
        await Backfill(app, "alert-refollow-uah");

        bank.StatementsFail = true;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/accounts", new { followedExternalIds = Array.Empty<string>() })).StatusCode);
        app.Clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/accounts", new { followedExternalIds = new[] { "alert-refollow-uah" } })).StatusCode);
        await Drain(app, owner);

        await SendAlerts(app);
        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Empty(Alerts(telegram));
        Assert.Equal("Healthy", (await Health(owner))!["state"]!.GetValue<string>());

        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Equal([string.Format(StaleText, "11.03.2075")], Alerts(telegram));
    }

    [Fact]
    public async Task A_backfill_with_no_batch_is_judged_from_its_start()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-nobatch", ("alert-nobatch-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2076, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-nobatch");
        await LinkTelegram(app, owner, telegram);
        await Backfill(app, "alert-nobatch-uah");
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.ImportBatches.Where(row => database.BankAccounts
                .Any(account => account.Id == row.BankAccountId && account.ExternalId == "alert-nobatch-uah")).ExecuteDeleteAsync();
            await database.BankAccounts.Where(row => row.ExternalId == "alert-nobatch-uah")
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.BackfillStartedAt, app.Clock.GetUtcNow()));
        }

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Empty(Alerts(telegram));

        app.Clock.Advance(TimeSpan.FromDays(2));
        await SendAlerts(app);
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Equal([string.Format(StaleText, "01.03.2076")], Alerts(telegram));
    }

    [Fact]
    public async Task A_completed_account_is_judged_by_its_cursor_and_not_by_a_recent_batch()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-done", ("alert-done-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2073, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-done");
        await LinkTelegram(app, owner, telegram);
        var now = app.Clock.GetUtcNow();
        await SetCursors(app, ("alert-done-uah", now.AddDays(-5)));
        await AddBatch(app, "alert-done-uah");

        await SendAlerts(app);

        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
        Assert.Single(Alerts(telegram));
    }

    [Fact]
    public async Task A_sync_a_day_late_raises_no_alert()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-late", ("alert-late-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2062, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-late");
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
        using var owner = await Connect(app, _ownerEmail, "token-alert-revoked");
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
        using var owner = await Connect(app, _ownerEmail, "token-alert-unreadable");
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
    public async Task A_recovery_under_way_does_not_alert_again_while_an_account_is_still_syncing()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-two", ("alert-two-a", 980), ("alert-two-b", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2067, 3, 10, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-two");
        await LinkTelegram(app, owner, telegram);
        var now = app.Clock.GetUtcNow();
        await SetCursors(app, ("alert-two-a", now.AddDays(-5)), ("alert-two-b", now.AddDays(-4)));

        await SendAlerts(app);
        Assert.Single(Alerts(telegram));

        // The first account recovers and the oldest cursor moves to the second's; its sync is still running.
        await SetCursors(app, ("alert-two-a", now));
        bank.RateLimit(1000, TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync("/api/monobank/sync", null)).StatusCode);
        Assert.Contains((await Status(owner)).Accounts, account => account.SyncPending);
        await SendAlerts(app);

        Assert.Single(Alerts(telegram));
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_second_rejection_of_the_same_token_keeps_the_first_time()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-twice", ("alert-twice-uah", 980));
        await using var app = CreateAlerting(At(2068, 3, 1, 10), bank, new StubTelegramHandler());
        using var owner = await Connect(app, _ownerEmail, "token-alert-twice");

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var import = scope.ServiceProvider.GetRequiredService<MonobankStatementImport>();
        var ownerId = await database.Users.Where(user => user.Email == _ownerEmail).Select(user => user.Id).SingleAsync();
        var connection = await database.MonobankConnections.AsNoTracking().SingleAsync(row => row.UserId == ownerId);

        Assert.True(await import.RejectAsync(connection, CancellationToken.None));
        var first = (await Status(owner)).TokenRejectedAt;
        app.Clock.Advance(TimeSpan.FromHours(2));
        Assert.True(await import.RejectAsync(connection, CancellationToken.None));

        Assert.NotNull(first);
        Assert.Equal(first, (await Status(owner)).TokenRejectedAt);
    }

    [Fact]
    public async Task No_alert_is_claimed_or_sent_without_a_channel_and_the_dashboard_still_says_so()
    {
        var bank = new FakeBank();
        bank.Connect("token-alert-nochannel", ("alert-nochannel-uah", 980));
        var telegram = new StubTelegramHandler();
        await using var app = CreateAlerting(At(2065, 3, 1, 10), bank, telegram);
        using var owner = await Connect(app, _ownerEmail, "token-alert-nochannel");

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(4));
        await SendAlerts(app);

        Assert.Empty(Alerts(telegram));
        Assert.Empty(await Incidents(app));
        Assert.Equal("Stale", (await Health(owner))!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_dashboard_has_no_sync_health_alert_without_a_connection()
    {
        await using var app = Create(At(2066, 3, 1, 10), new FakeBank());
        using var owner = await ApiFixture.SignIn(app.Factory, _ownerEmail);

        Assert.Null(await Health(owner));
    }

    // The sync worker runs; the reminder worker, the Telegram poller and the nightly sync do not, so the test
    // decides when alerts are sent, when the bot is polled and when a sync starts. A clock moved past 03:00
    // would otherwise queue a nightly sync on another thread, and a stale alert is held back while one is in
    // flight (ADR-026), so the pass that should alert could find it queued or not.
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
                        && (type == typeof(ReminderWorker) || type == typeof(TelegramPollWorker) || type == typeof(MonobankNightlySync)))
                    .ToArray())
                {
                    services.Remove(worker);
                }
            }));
        return new SyncApp(factory, clock, handler);
    }

    private async Task LinkTelegram(SyncApp app, HttpClient owner, StubTelegramHandler telegram)
    {
        await TelegramSteps.ForgetPollOffset(app.Factory);
        var next = telegram.Updates.Count == 0 ? 10 : telegram.Updates.Max(update => update["update_id"]!.GetValue<long>()) + 1;
        telegram.Updates.Add(StubTelegramHandler.Update(next, _chat, $"/start {TelegramSteps.CodeOf(await TelegramSteps.Connect(owner))}"));
        await TelegramSteps.Poll(app.Factory);
        Assert.True((await TelegramSteps.Channel(owner))["linked"]!.GetValue<bool>());
        telegram.ClearCalls();
    }

    private static Task SendAlerts(SyncApp app) =>
        app.Factory.Services.GetRequiredService<ReminderSender>().RunOnceAsync(CancellationToken.None);

    private List<string> Alerts(StubTelegramHandler telegram) =>
        [
            .. telegram.To("sendMessage")
                .Where(call => call.Body["chat_id"]!.GetValue<string>() == _chat.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Select(call => call.Body["text"]!.GetValue<string>()),
        ];

    private static async Task<JsonObject?> Health(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonObject>("/api/dashboard"))!["sync"]?.AsObject();

    private async Task<List<SentReminder>> Incidents(SyncApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = database.Users.Where(user => user.Email == _ownerEmail).Select(user => user.Id);
        return await database.SentReminders.AsNoTracking()
            .Where(row => owner.Contains(row.UserId) && row.Incident != string.Empty)
            .ToListAsync();
    }

    // Puts the account back to still backfilling, its only progress the window the connection just imported.
    private static async Task Backfill(SyncApp app, string externalId)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.BankAccounts
            .Where(row => row.ExternalId == externalId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.HistoryImportedAt, (DateTimeOffset?)null));
        var account = await database.BankAccounts.AsNoTracking().SingleAsync(row => row.ExternalId == externalId);
        await database.ImportBatches
            .Where(row => row.BankAccountId == account.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, app.Clock.GetUtcNow()));
    }

    private static async Task AddBatch(SyncApp app, string externalId)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await database.BankAccounts.AsNoTracking().SingleAsync(row => row.ExternalId == externalId);
        database.ImportBatches.Add(new ImportBatch
        {
            Id = Guid.NewGuid(),
            UserId = account.UserId,
            Source = ImportSource.Monobank,
            BankAccountId = account.Id,
            From = app.Clock.GetUtcNow().AddDays(-30),
            To = app.Clock.GetUtcNow(),
            CreatedAt = app.Clock.GetUtcNow(),
        });
        await database.SaveChangesAsync();
    }

    private async Task SetCursors(SyncApp app, params (string ExternalId, DateTimeOffset At)[] cursors)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (externalId, at) in cursors)
        {
            await database.BankAccounts
                .Where(row => row.ExternalId == externalId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.SyncedThrough, at)
                    .SetProperty(row => row.HistoryImportedAt, at));
        }
    }
}
