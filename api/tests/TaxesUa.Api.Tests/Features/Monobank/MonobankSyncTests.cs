using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Monobank;

// The owners are shared by every test in this class, so each test runs in a year of its own and names
// accounts no other test uses; a token save deactivates every account the new client-info omits.
public sealed class MonobankSyncTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Receipts_in_each_currency_are_recorded_as_a_manual_entry_would_be_with_paced_calls()
    {
        var bank = new FakeBank();
        bank.Connect("token-currencies", ("cur-uah", 980), ("cur-usd", 840), ("cur-eur", 978));
        bank.Put("cur-uah", new Operation("op-uah", At(2032, 7, 10, 9), 1_234_56, 980, CounterName: "Client UA"));
        bank.Put("cur-usd", new Operation("op-usd", At(2032, 7, 12, 12), 1_000_00, 840, CounterName: "Acme Inc",
            Description: "Invoice 7", Comment: "July services"));
        bank.Put("cur-eur", new Operation("op-eur", At(2032, 7, 13, 12), 500_00, 978, CounterName: "Beta GmbH"));
        var nbu = Nbu(("USD", new DateOnly(2032, 7, 12), "41.2345"), ("EUR", new DateOnly(2032, 7, 13), "45.6789"));
        await using var app = Create(At(2032, 7, 15, 10), bank, nbu);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-currencies");

        await Sync(app, owner);

        var imported = (await List(owner, 2032)).Items.Where(item => item.Source is not null).ToArray();
        Assert.Equal(3, imported.Length);
        var usd = imported.Single(item => item.Currency == Currency.USD);
        Assert.Equal((new DateOnly(2032, 7, 12), 1_000_00L, 412_345, RateSource.Nbu, 4_123_450L), (usd.ValueDate, usd.AmountMinor, usd.RateE4, usd.RateSource!.Value, usd.AmountUahKop));
        Assert.Equal(TransactionKind.Income, usd.Kind);
        Assert.Equal("Acme Inc", usd.ClientName);
        Assert.Equal("Invoice 7 · July services", usd.Description);
        Assert.Equal(new TransactionSource(Bank.Monobank, "USD"), usd.Source);
        var eur = imported.Single(item => item.Currency == Currency.EUR);
        Assert.Equal((456_789, 2_283_945L), (eur.RateE4, eur.AmountUahKop));
        var uah = imported.Single(item => item.Currency == Currency.UAH);
        Assert.Equal((1_234_56L, 1_234_56L, (RateSource?)null), (uah.AmountMinor, uah.AmountUahKop, uah.RateSource));

        var manual = await Post(owner, new TransactionRequest(
            new DateOnly(2032, 7, 12), 1_000_00, Currency.USD, null, TransactionKind.Income, null, "Acme Inc", null, null, null));
        Assert.Equal((manual.RateE4, manual.RateDate, manual.AmountUahKop), (usd.RateE4, usd.RateDate, usd.AmountUahKop));
        Assert.Null(manual.Source);

        var statementCalls = bank.StatementCalls(app.Handler);
        Assert.Equal(3, statementCalls.Length);
        Assert.All(statementCalls.Zip(statementCalls.Skip(1)), pair =>
            Assert.True(pair.Second.At - pair.First.At >= TimeSpan.FromSeconds(60), $"{pair.First.At:O} then {pair.Second.At:O}"));

        var status = await Status(owner);
        Assert.All(
            status.Accounts.Where(account => account.ExternalId.StartsWith("cur-", StringComparison.Ordinal)),
            account => Assert.Equal((1, 0), Counts(account)));
    }

    [Fact]
    public async Task An_imported_row_counts_toward_income_waits_for_review_and_writes_a_create_entry()
    {
        var bank = new FakeBank();
        bank.Connect("token-audit", ("audit-uah", 980));
        bank.Put("audit-uah", new Operation("op-audit", At(2033, 3, 3, 9), 2_000_00, 980, CounterName: "Gamma"));
        await using var app = Create(At(2033, 3, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-audit");
        var settings = new SettingsRequest(
            new DateOnly(2030, 1, 1), PaymentMode.Quarterly, EsvRegistrationMonthPolicy.FullMonth, false, true, true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday], "uk", "system", "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", settings, Json)).StatusCode);

        await Sync(app, owner);

        var list = await List(owner, 2033);
        var row = Assert.Single(list.Items);
        Assert.Equal(2_000_00, list.TotalIncomeKop);

        var history = await owner.GetFromJsonAsync<AuditEntryResponse[]>(
            $"/api/audit?entity=Transaction&id={row.Id}", Json);
        var create = Assert.Single(history!);
        Assert.Equal(AuditAction.Create, create.Action);
        Assert.Equal("NeedsReview", create.After!["reviewStatus"].GetString());
        Assert.Equal("op-audit", create.After["externalId"].GetString());
        Assert.Equal("Gamma", create.After["counterparty"].GetString());
    }

    [Fact]
    public async Task Repeated_and_overlapping_syncs_add_nothing_and_an_owner_edit_survives()
    {
        var bank = new FakeBank();
        bank.Connect("token-repeat", ("repeat-uah", 980));
        bank.Put("repeat-uah", new Operation("op-repeat", At(2034, 5, 1, 9), 300_00, 980, CounterName: "Delta"));
        var clock = At(2034, 5, 10, 10);
        await using var app = Create(clock, bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-repeat");
        await Sync(app, owner);
        var row = Assert.Single((await List(owner, 2034)).Items);

        var edit = new TransactionRequest(
            row.ValueDate, 250_00, Currency.UAH, null, TransactionKind.OwnTransfer, "My own card", "Delta", null, "edited", null);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/transactions/{row.Id}", edit, Json)).StatusCode);

        await Sync(app, owner);
        app.Clock.Advance(TimeSpan.FromDays(3));
        await Sync(app, owner);

        var after = Assert.Single((await List(owner, 2034)).Items);
        Assert.Equal((row.Id, 250_00L, TransactionKind.OwnTransfer, "edited"), (after.Id, after.AmountMinor, after.Kind, after.Description));
        Assert.Equal(new TransactionSource(Bank.Monobank, "UAH"), after.Source);
        Assert.Equal((0, 0), Counts(await Status(owner), "repeat-uah"));
    }

    [Fact]
    public async Task A_held_operation_is_skipped_then_imported_once_settled()
    {
        var bank = new FakeBank();
        bank.Connect("token-hold", ("hold-uah", 980));
        bank.Put("hold-uah", new Operation("op-hold", At(2035, 2, 1, 9), 700_00, 980, Hold: true));
        await using var app = Create(At(2035, 2, 2, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-hold");

        await Sync(app, owner);
        Assert.Empty((await List(owner, 2035)).Items);
        Assert.Equal((0, 1), Counts(await Status(owner), "hold-uah"));

        bank.Put("hold-uah", new Operation("op-hold", At(2035, 2, 1, 9), 700_00, 980));
        await Sync(app, owner);

        Assert.Equal(700_00, Assert.Single((await List(owner, 2035)).Items).AmountMinor);
        Assert.Equal((1, 0), Counts(await Status(owner), "hold-uah"));
    }

    [Fact]
    public async Task Only_settled_credits_in_a_known_currency_dated_by_Kyiv_are_recorded()
    {
        var bank = new FakeBank();
        bank.Connect("token-dates", ("dates-uah", 980), ("dates-pln", 985));
        bank.Put("dates-uah", new Operation("op-late", new DateTimeOffset(2036, 7, 20, 23, 30, 0, TimeSpan.Zero), 100_00, 980));
        bank.Put("dates-uah", new Operation("op-debit", At(2036, 7, 21, 9), -50_00, 980));
        bank.Put("dates-pln", new Operation("op-pln", At(2036, 7, 21, 9), 90_00, 985));
        await using var app = Create(At(2036, 7, 25, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-dates");

        await Sync(app, owner);

        var row = Assert.Single((await List(owner, 2036)).Items);
        Assert.Equal(new DateOnly(2036, 7, 21), row.ValueDate);
        var status = await Status(owner);
        Assert.Equal((1, 0), Counts(status, "dates-uah"));
        Assert.Equal((0, 1), Counts(status, "dates-pln"));
    }

    [Fact]
    public async Task A_card_payment_in_another_currency_is_recorded_in_the_account_currency()
    {
        var bank = new FakeBank();
        bank.Connect("token-card", ("card-uah", 980));
        bank.Put("card-uah", new Operation("op-card", At(2042, 3, 2, 9), 4_500_000, 978, CounterName: "EU client"));
        await using var app = Create(At(2042, 3, 3, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-card");

        await Sync(app, owner);

        var row = Assert.Single((await List(owner, 2042)).Items);
        Assert.Equal((Currency.UAH, 4_500_000L, 4_500_000L), (row.Currency, row.AmountMinor, row.AmountUahKop));
    }

    [Fact]
    public async Task A_long_counterparty_name_is_cut_without_splitting_an_emoji()
    {
        var name = new string('a', TransactionsEndpoints.MaxClientNameLength - 1) + "\U0001F600 tail";
        var bank = new FakeBank();
        bank.Connect("token-emoji", ("emoji-uah", 980));
        bank.Put("emoji-uah", new Operation("op-emoji", At(2047, 5, 7, 9), 100_00, 980, CounterName: name));
        await using var app = Create(At(2047, 5, 8, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-emoji");

        await Sync(app, owner);

        Assert.Single((await List(owner, 2047)).Items);
        Assert.Equal((1, 0), Counts(await Status(owner), "emoji-uah"));
    }

    [Fact]
    public async Task A_failed_statement_call_clears_the_queue_and_the_next_sync_imports()
    {
        var bank = new FakeBank();
        bank.Connect("token-fail", ("fail-uah", 980));
        bank.Put("fail-uah", new Operation("op-fail", At(2043, 5, 2, 9), 123_00, 980));
        bank.StatementsFail = true;
        await using var app = Create(At(2043, 5, 3, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-fail");

        await Sync(app, owner);
        Assert.Empty((await List(owner, 2043)).Items);
        Assert.Null((await Status(owner)).Accounts.Single(account => account.ExternalId == "fail-uah").LastSync);

        bank.StatementsFail = false;
        await Sync(app, owner);
        Assert.Equal(123_00, Assert.Single((await List(owner, 2043)).Items).AmountMinor);
    }

    [Fact]
    public async Task An_unfollowed_account_is_never_fetched()
    {
        var bank = new FakeBank();
        bank.Connect("token-follow", ("follow-a", 980), ("follow-b", 980));
        bank.Put("follow-b", new Operation("op-b", At(2037, 4, 1, 9), 100_00, 980));
        await using var app = Create(At(2037, 4, 2, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-follow");
        await owner.PutAsJsonAsync("/api/monobank/accounts", new { followedExternalIds = new[] { "follow-a" } });

        await Sync(app, owner);

        Assert.All(bank.StatementCalls(app.Handler), call => Assert.Contains("/follow-a/", call.Uri.AbsolutePath, StringComparison.Ordinal));
        Assert.Empty((await List(owner, 2037)).Items);
    }

    [Fact]
    public async Task A_full_statement_page_is_followed_by_a_paced_call_for_the_older_operations()
    {
        var bank = new FakeBank();
        bank.Connect("token-page", ("page-uah", 980));
        var now = At(2038, 6, 30, 10);
        for (var i = 0; i < MonobankClient.StatementPageSize; i++)
        {
            bank.Put("page-uah", new Operation($"debit-{i}", now.AddHours(-1).AddSeconds(-i), -1_00, 980));
        }

        bank.Put("page-uah", new Operation("op-old-credit", now.AddDays(-20), 900_00, 980));
        await using var app = Create(now, bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-page");

        await Sync(app, owner);

        Assert.Equal(900_00, Assert.Single((await List(owner, 2038)).Items).AmountMinor);
        var calls = bank.StatementCalls(app.Handler);
        Assert.Equal(2, calls.Length);
        Assert.True(calls[1].At - calls[0].At >= TimeSpan.FromSeconds(60));
        Assert.EndsWith($"/{now.AddHours(-1).AddSeconds(1 - MonobankClient.StatementPageSize).ToUnixTimeSeconds()}", calls[1].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_owners_sync_never_reads_or_writes_another_owners_accounts()
    {
        var bank = new FakeBank();
        bank.Connect("token-owner-a", ("iso-a", 980));
        bank.Connect("token-owner-b", ("iso-b", 980));
        bank.Put("iso-a", new Operation("op-a", At(2039, 8, 1, 9), 111_00, 980));
        bank.Put("iso-b", new Operation("op-b", At(2039, 8, 1, 9), 222_00, 980));
        await using var app = Create(At(2039, 8, 2, 10), bank);
        using var first = await Connect(app, ApiFixture.AllowedEmail, "token-owner-a");
        using var second = await Connect(app, ApiFixture.SecondAllowedEmail, "token-owner-b");

        await Sync(app, first);

        Assert.Equal(111_00, Assert.Single((await List(first, 2039)).Items).AmountMinor);
        Assert.Empty((await List(second, 2039)).Items);
        Assert.All(bank.StatementCalls(app.Handler), call => Assert.Contains("/iso-a/", call.Uri.AbsolutePath, StringComparison.Ordinal));

        await Sync(app, second);
        Assert.Equal(222_00, Assert.Single((await List(second, 2039)).Items).AmountMinor);
        Assert.Single((await List(first, 2039)).Items);
    }

    [Fact]
    public async Task A_backup_carries_accounts_and_import_fields_but_no_token_and_a_restore_then_sync_adds_nothing()
    {
        var bank = new FakeBank();
        bank.Connect("token-backup-secret", ("backup-usd", 840));
        bank.Put("backup-usd", new Operation("op-backup", At(2050, 9, 3, 9), 250_00, 840, CounterName: "Epsilon"));
        await using var app = Create(At(2050, 9, 5, 10), bank, Nbu(("USD", new DateOnly(2050, 9, 3), "40.0000")));
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-backup-secret");
        await Sync(app, owner);
        var imported = Assert.Single((await List(owner, 2050)).Items);

        var file = await owner.GetStringAsync("/api/backup");

        Assert.DoesNotContain("token-backup-secret", file, StringComparison.Ordinal);
        Assert.DoesNotContain("encryptedToken", file, StringComparison.OrdinalIgnoreCase);
        var document = Parse(file);
        Assert.Equal(2, document.GetProperty("schemaVersion").GetInt32());
        var account = document.GetProperty("bankAccounts").EnumerateArray()
            .Single(row => row.GetProperty("externalId").GetString() == "backup-usd");
        Assert.True(account.GetProperty("isActive").GetBoolean());
        var transaction = document.GetProperty("transactions").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == imported.Id);
        Assert.Equal("op-backup", transaction.GetProperty("externalId").GetString());
        Assert.Equal("NeedsReview", transaction.GetProperty("reviewStatus").GetString());
        Assert.Equal(account.GetProperty("id").GetGuid(), transaction.GetProperty("bankAccountId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, transaction.GetProperty("importBatchId").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        var restore = await owner.PostAsync(
            "/api/restore", new StringContent(file, System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        Assert.False((await Status(owner)).Connected);
        Assert.Equal(file, await owner.GetStringAsync("/api/backup"));

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-backup-secret" })).StatusCode);
        await Sync(app, owner);

        Assert.Equal(imported.Id, Assert.Single((await List(owner, 2050)).Items).Id);
        Assert.Equal((0, 0), Counts(await Status(owner), "backup-usd"));
    }

    [Fact]
    public async Task Syncing_needs_a_connection_and_a_session()
    {
        await using var app = Create(At(2041, 1, 5, 10), new FakeBank());
        using var anonymous = ApiFixture.CreateClient(app.Factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/monobank/sync", null)).StatusCode);

        using var owner = await ApiFixture.SignIn(app.Factory, ApiFixture.SecondAllowedEmail);
        await owner.DeleteAsync("/api/monobank/connection");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/monobank/sync", null)).StatusCode);
    }

    private static DateTimeOffset At(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static (int Imported, int Skipped) Counts(MonobankConnectionResponse status, string externalId) =>
        Counts(status.Accounts.Single(account => account.ExternalId == externalId));

    private static (int Imported, int Skipped) Counts(MonobankAccountResponse account) =>
        (account.LastSync!.ImportedCount, account.LastSync.SkippedCount);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static StubNbuHandler Nbu(params (string Currency, DateOnly Date, string Rate)[] rates) =>
        new(request =>
        {
            var query = request.RequestUri!.Query;
            var match = rates.FirstOrDefault(rate =>
                query.Contains($"valcode={rate.Currency}", StringComparison.Ordinal)
                && query.Contains($"date={rate.Date:yyyyMMdd}", StringComparison.Ordinal));
            return StubNbuHandler.Json(match.Currency is null ? "[]" : StubNbuHandler.Row(match.Currency, match.Date, match.Rate));
        });

    private SyncApp Create(DateTimeOffset now, FakeBank bank, StubNbuHandler? nbu = null)
    {
        var clock = new FakeTimeProvider(now);
        var handler = new StubMonobankHandler(bank.Respond, clock);
        var factory = fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient<NbuRateClient>().ConfigurePrimaryHttpMessageHandler(() => nbu ?? Nbu());
        }));
        return new SyncApp(factory, clock, handler);
    }

    private static async Task<HttpClient> Connect(SyncApp app, string email, string token)
    {
        var owner = await ApiFixture.SignIn(app.Factory, email);
        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return owner;
    }

    // The worker waits on the fake clock between statement calls, so the clock is moved on while the
    // queue drains; real time only bounds how long the test is willing to wait.
    private static async Task Sync(SyncApp app, HttpClient owner)
    {
        var response = await owner.PostAsync("/api/monobank/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await Status(owner)).Accounts.Any(account => account.SyncPending))
        {
            Assert.True(DateTime.UtcNow < deadline, "the sync queue did not drain");
            app.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }
    }

    private static async Task<MonobankConnectionResponse> Status(HttpClient owner) =>
        (await owner.GetFromJsonAsync<MonobankConnectionResponse>("/api/monobank/connection", Json))!;

    private static async Task<TransactionListResponse> List(HttpClient owner, int year) =>
        (await owner.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?year={year}", Json))!;

    private static async Task<TransactionResponse> Post(HttpClient owner, TransactionRequest request)
    {
        var response = await owner.PostAsJsonAsync("/api/transactions", request, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private sealed record SyncApp(WebApplicationFactory<Program> Factory, FakeTimeProvider Clock, StubMonobankHandler Handler)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed record Operation(
        string Id,
        DateTimeOffset Time,
        long Amount,
        int CurrencyCode,
        bool Hold = false,
        string? CounterName = null,
        string? Description = null,
        string? Comment = null)
    {
        public object ToJson() => new
        {
            id = Id,
            time = Time.ToUnixTimeSeconds(),
            description = Description ?? string.Empty,
            mcc = 4829,
            originalMcc = 4829,
            hold = Hold,
            amount = Amount,
            operationAmount = Amount,
            currencyCode = CurrencyCode,
            commissionRate = 0,
            cashbackAmount = 0,
            balance = 0,
            comment = Comment,
            counterEdrpou = "12345678",
            counterIban = "UA000000000000000000000000000",
            counterName = CounterName,
        };
    }

    // A monobank that knows its tokens' accounts and answers a statement the way the real one does:
    // operations inside the window, newest first, at most one page.
    private sealed class FakeBank
    {
        private readonly ConcurrentDictionary<string, string> _clientInfo = new();

        public bool StatementsFail { get; set; }

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Operation>> _operations = new();

        public void Connect(string token, params (string Id, int CurrencyCode)[] accounts) =>
            _clientInfo[token] = StubMonobankHandler.ClientInfo(
                $"client-{token}",
                [.. accounts.Select(account => (account.Id, "fop", account.CurrencyCode, $"UA{account.Id}"))]);

        public void Put(string accountId, Operation operation) =>
            _operations.GetOrAdd(accountId, _ => new())[operation.Id] = operation;

        public (Uri Uri, DateTimeOffset At)[] StatementCalls(StubMonobankHandler handler) =>
            [.. handler.Calls.Where(call => call.Uri.AbsolutePath.Contains("/statement/", StringComparison.Ordinal))];

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var token = request.Headers.TryGetValues("X-Token", out var values) ? values.First() : string.Empty;
            if (!_clientInfo.TryGetValue(token, out var clientInfo))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            var path = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (path is ["personal", "client-info"])
            {
                return StubMonobankHandler.Json(clientInfo);
            }

            if (path is not ["personal", "statement", var accountId, var fromText, var toText])
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (StatementsFail)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            var from = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fromText, CultureInfo.InvariantCulture));
            var to = DateTimeOffset.FromUnixTimeSeconds(long.Parse(toText, CultureInfo.InvariantCulture));
            Assert.True(to - from <= TimeSpan.FromDays(31) + TimeSpan.FromHours(1), "the window is wider than monobank allows");
            var page = _operations.GetValueOrDefault(accountId)?.Values
                .Where(operation => operation.Time >= from && operation.Time <= to)
                .OrderByDescending(operation => operation.Time)
                .Take(MonobankClient.StatementPageSize)
                .Select(operation => operation.ToJson())
                .ToArray() ?? [];
            return StubMonobankHandler.Json(JsonSerializer.Serialize(page));
        }
    }
}
