using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed partial class MonobankSyncTests
{
    private const string PublicBaseUrl = "https://taxes.example.test";

    [Fact]
    public async Task Without_a_public_base_url_no_webhook_is_registered_or_removed()
    {
        var bank = new FakeBank();
        bank.Connect("token-nohook", ("nohook-uah", 980));
        await using var app = Create(At(2071, 3, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-nohook");

        Assert.Equal(new WebhookStatusResponse(WebhookState.Off, null), (await Status(owner)).Webhook);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(100);

        Assert.Empty(bank.Webhooks);
        Assert.Null((await Status(owner)).Webhook);
    }

    [Fact]
    public async Task A_token_save_registers_a_secret_url_the_bank_check_reaches_and_a_new_token_rotates_it()
    {
        var bank = new FakeBank();
        bank.Connect("token-hook", ("hook-uah", 980));
        await using var app = Create(At(2072, 3, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-hook");
        using var monobank = ApiFixture.CreateClient(app.Factory);

        Assert.Equal(WebhookState.Registered, (await WebhookSettled(app, owner)).State);
        var first = Assert.Single(bank.Webhooks.Select(webhook => webhook.Url).Distinct());
        Assert.Matches(new Regex($"^{Regex.Escape(PublicBaseUrl)}/api/monobank/webhook/[0-9a-f]{{64}}$"), first);
        Assert.Equal(HttpStatusCode.OK, (await monobank.GetAsync(new Uri(first).AbsolutePath)).StatusCode);

        bank.Connect("token-hook-new", ("hook-uah", 980));
        var replaced = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-hook-new" });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(WebhookState.Registered, (await WebhookSettled(app, owner)).State);

        var second = bank.Webhooks.Last(webhook => webhook.Token == "token-hook-new").Url;
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await monobank.GetAsync(new Uri(first).AbsolutePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await monobank.GetAsync(new Uri(second).AbsolutePath)).StatusCode);
    }

    [Fact]
    public async Task A_webhook_post_queues_a_statement_read_and_its_forged_body_inserts_nothing()
    {
        var bank = new FakeBank();
        bank.Connect("token-signal", ("signal-uah", 980));
        await using var app = Create(At(2050, 4, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-signal");
        await WebhookSettled(app, owner);
        using var monobank = ApiFixture.CreateClient(app.Factory);
        var path = RegisteredPath(bank);
        bank.Put("signal-uah", new Operation("op-signal", At(2050, 4, 5, 9), 321_00, 980, CounterName: "Real client"));

        var forged = JsonSerializer.Serialize(new
        {
            type = "StatementItem",
            data = new
            {
                account = "signal-uah",
                statementItem = new Operation("op-forged", At(2050, 4, 5, 9), 999_000_00, 980, CounterName: "Forger").ToJson(),
            },
        });
        var posted = await monobank.PostAsync(path, new StringContent(forged, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        await Drain(app, owner);

        var row = Assert.Single((await List(owner, 2050)).Items);
        Assert.Equal((321_00L, "Real client"), (row.AmountMinor, row.ClientName));
        Assert.Equal((1, 0), Counts(await Status(owner), "signal-uah"));
    }

    [Fact]
    public async Task An_unknown_secret_gets_404_and_queues_nothing()
    {
        var bank = new FakeBank();
        bank.Connect("token-stranger", ("stranger-uah", 980));
        await using var app = Create(At(2074, 5, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-stranger");
        await WebhookSettled(app, owner);
        using var monobank = ApiFixture.CreateClient(app.Factory);
        var calls = bank.StatementCalls(app.Handler).Length;

        foreach (var secret in new[] { MonobankWebhooks.NewSecret(), "not-a-secret", string.Concat(Enumerable.Repeat("0", 64)) })
        {
            var path = MonobankWebhooks.PathPrefix + secret;
            Assert.Equal(HttpStatusCode.NotFound, (await monobank.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await monobank.PostAsync(path, new StringContent("{}"))).StatusCode);
        }

        Assert.DoesNotContain((await Status(owner)).Accounts, account => account.SyncPending);
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(100);
        Assert.Equal(calls, bank.StatementCalls(app.Handler).Length);
    }

    [Fact]
    public async Task A_flood_of_webhook_posts_costs_at_most_one_sync_beyond_the_running_one()
    {
        var bank = new FakeBank();
        bank.Connect("token-flood", ("flood-uah", 980));
        await using var app = Create(At(2075, 6, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-flood");
        await WebhookSettled(app, owner);
        using var monobank = ApiFixture.CreateClient(app.Factory);
        var path = RegisteredPath(bank);
        var calls = bank.StatementCalls(app.Handler).Length;

        var answers = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => monobank.PostAsync(path, null)));
        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        await Drain(app, owner);

        Assert.InRange(bank.StatementCalls(app.Handler).Length - calls, 1, 2);
    }

    [Fact]
    public async Task A_failed_registration_is_shown_and_never_blocks_the_connection()
    {
        var bank = new FakeBank { WebhookFails = true };
        bank.Connect("token-hookfail", ("hookfail-uah", 980));
        bank.Put("hookfail-uah", new Operation("op-hookfail", At(2059, 2, 5, 9), 12_00, 980));
        await using var app = Create(At(2059, 2, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-hookfail");

        var webhook = await WebhookSettled(app, owner);
        Assert.Equal(WebhookState.Failed, webhook.State);
        Assert.Equal(SyncFailure.BankError, webhook.LastFailure!.Reason);
        var status = await Status(owner);
        Assert.True(status.Connected);
        Assert.Null(status.TokenRejectedAt);
        Assert.Equal(12_00, Assert.Single((await List(owner, 2059)).Items).AmountMinor);
    }

    [Fact]
    public async Task A_token_the_webhook_call_rejects_stops_syncing_and_shows_the_webhook_failed()
    {
        var bank = new FakeBank { WebhookRejects = true };
        bank.Connect("token-hookreject", ("hookreject-uah", 980));
        await using var app = Create(At(2080, 2, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-hookreject");

        var webhook = await WebhookSettled(app, owner);
        Assert.Equal(new WebhookStatusResponse(WebhookState.Failed, null), webhook);
        Assert.NotNull((await Status(owner)).TokenRejectedAt);
    }

    [Fact]
    public async Task Disconnecting_removes_the_webhook_at_the_bank_and_its_url_stops_answering()
    {
        var bank = new FakeBank();
        bank.Connect("token-unhook", ("unhook-uah", 980));
        await using var app = Create(At(2077, 7, 5, 10), bank, publicBaseUrl: PublicBaseUrl);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-unhook");
        await WebhookSettled(app, owner);
        using var monobank = ApiFixture.CreateClient(app.Factory);
        var path = RegisteredPath(bank);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await monobank.GetAsync(path)).StatusCode);
        var steps = 0;
        while (bank.Webhooks[^1].Url.Length > 0)
        {
            Assert.True(++steps < MaxDrainSteps, "the webhook was not removed");
            app.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }

        Assert.Equal(("token-unhook", string.Empty), bank.Webhooks[^1]);
    }

    [Fact]
    public async Task The_nightly_run_at_three_in_Kyiv_imports_an_operation_no_webhook_announced()
    {
        var bank = new FakeBank();
        bank.Connect("token-nightly", ("nightly-uah", 980));
        await using var app = Create(At(2040, 6, 10, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-nightly");
        bank.Put("nightly-uah", new Operation("op-nightly", At(2040, 6, 10, 12), 77_00, 980));
        var calls = bank.StatementCalls(app.Handler).Length;

        app.Clock.Advance(new DateTimeOffset(2040, 6, 10, 23, 59, 0, TimeSpan.Zero) - app.Clock.GetUtcNow());
        await Task.Delay(200);
        Assert.Equal(calls, bank.StatementCalls(app.Handler).Length);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        var steps = 0;
        while ((await List(owner, 2040)).Items.Length == 0)
        {
            Assert.True(++steps < MaxDrainSteps, "the nightly run did not import");
            app.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }

        Assert.Equal(77_00, Assert.Single((await List(owner, 2040)).Items).AmountMinor);
        // The drain loop advances the clock while the worker is still reading, so the call lands at
        // the run's instant or a few fake seconds after it, never before.
        var at = bank.StatementCalls(app.Handler)[calls].At;
        var run = new DateTimeOffset(2040, 6, 11, 0, 0, 0, TimeSpan.Zero);
        Assert.InRange(at, run, run.AddMinutes(1));
    }

    [Fact]
    public async Task Imported_history_stays_imported_after_forty_days_without_a_successful_sync()
    {
        var bank = new FakeBank();
        bank.Connect("token-aged", ("aged-uah", 980));
        await using var app = Create(At(2079, 3, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-aged");
        Assert.True((await Status(owner)).Accounts.Single(account => account.ExternalId == "aged-uah").BackfillComplete);

        bank.StatementsFail = true;
        app.Clock.Advance(TimeSpan.FromDays(40));
        using var again = await ApiFixture.SignIn(app.Factory, ApiFixture.AllowedEmail);
        await DrainUntil(app, again, "aged-uah", account => account.LastFailure is not null && !account.SyncPending);

        var account = (await Status(again)).Accounts.Single(row => row.ExternalId == "aged-uah");
        Assert.True(account.SyncedThrough < app.Clock.GetUtcNow() - TimeSpan.FromDays(31));
        Assert.True(account.BackfillComplete);
    }

    private static string RegisteredPath(FakeBank bank) => new Uri(bank.Webhooks[^1].Url).AbsolutePath;

    // Moves the clock on until the registration queued by the last token save has an answer.
    private static async Task<WebhookStatusResponse> WebhookSettled(SyncApp app, HttpClient owner)
    {
        var steps = 0;
        while (true)
        {
            var webhook = (await Status(owner)).Webhook!;
            if (webhook.State is WebhookState.Registered or WebhookState.Failed)
            {
                return webhook;
            }

            Assert.True(++steps < MaxDrainSteps, "the webhook registration did not finish");
            app.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }
    }
}
