using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Monobank;

// Reviewing imports (#78). Same shared owners as the rest of the class, so each test keeps its own
// accounts and year, and counts the dashboard's review warning as a change rather than a total.
public sealed partial class MonobankSyncTests
{
    [Fact]
    public async Task Imported_credits_arrive_with_suggested_kinds_and_the_sold_currency_is_never_recorded()
    {
        var bank = new FakeBank();
        bank.Connect("token-suggest", ("sug-uah", 980), ("sug-usd", 840));
        bank.Put("sug-uah", new Operation("op-client", At(2044, 4, 1, 9), 1_000_00, 980, CounterName: "Client UA"));
        bank.Put("sug-uah", new Operation("op-own", At(2044, 4, 2, 9), 200_00, 980, CounterName: "Me", CounterIban: "UAsug-usd"));
        bank.Put("sug-uah", new Operation("op-sale", At(2044, 4, 3, 9).AddSeconds(2), 41_000_00, 980, CounterName: "monobank"));
        bank.Put("sug-usd", new Operation("op-sold", At(2044, 4, 3, 9), -1_000_00, 840));
        bank.Put("sug-usd", new Operation("op-usd", At(2044, 4, 4, 9), 500_00, 840, CounterName: "Acme Inc"));
        await using var app = Create(
            At(2044, 4, 5, 10),
            bank,
            Nbu(("USD", new DateOnly(2044, 4, 3), "41.2000"), ("USD", new DateOnly(2044, 4, 4), "41.0000")));
        using var owner = await Connect(app, _ownerEmail, "token-suggest");

        await Sync(app, owner);

        var rows = (await List(owner, 2044)).Items;
        Assert.Equal(4, rows.Length);
        Assert.All(rows, row => Assert.Equal(ReviewStatus.NeedsReview, row.ReviewStatus));
        Assert.Equal(
            [
                ("Acme Inc", TransactionKind.Income, (string?)null),
                ("monobank", TransactionKind.FxSale, "monobank: currency sale"),
                ("Me", TransactionKind.OwnTransfer, "monobank: own transfer"),
                ("Client UA", TransactionKind.Income, null),
            ],
            rows.Select(row => (row.ClientName!, row.Kind, row.NonIncomeReason)));
        Assert.DoesNotContain(rows, row => row.Currency == Currency.USD && row.AmountMinor == 1_000_00);

        var review = (await Review(owner)).Where(row => row.ValueDate.Year == 2044).Select(row => row.Id);
        Assert.Equal(rows.Select(row => row.Id), review);
    }

    [Fact]
    public async Task A_foreign_account_read_first_still_pairs_the_sale_once_the_hryvnia_leg_arrives()
    {
        var bank = new FakeBank();
        bank.Connect("token-late-leg", ("late-uah", 980), ("late-usd", 840));
        bank.Put("late-usd", new Operation("op-late-sold", At(2045, 2, 3, 9), -1_000_00, 840));
        await using var app = Create(At(2045, 2, 5, 10), bank, Nbu(("USD", new DateOnly(2045, 2, 3), "40.5000")));
        using var owner = await Connect(app, _ownerEmail, "token-late-leg");
        await Sync(app, owner);

        bank.Put("late-uah", new Operation("op-late-sale", At(2045, 2, 3, 9).AddSeconds(-5), 40_000_00, 980));
        bank.Put("late-uah", new Operation("op-late-client", At(2045, 2, 3, 9).AddSeconds(30), 300_00, 980));
        await Sync(app, owner);

        // A repeat reads the same debit again; it must stay with the leg it paired first rather than
        // drift to the receipt 30 seconds later.
        await Sync(app, owner);

        var rows = (await List(owner, 2045)).Items.ToDictionary(row => row.AmountMinor, row => row.Kind);
        Assert.Equal(TransactionKind.FxSale, rows[40_000_00]);
        Assert.Equal(TransactionKind.Income, rows[300_00]);
    }

    [Fact]
    public async Task A_sale_guess_returns_to_income_once_the_real_leg_settles_closer()
    {
        var bank = new FakeBank();
        bank.Connect("token-stale-sale", ("stale-uah", 980), ("stale-usd", 840));
        bank.Put("stale-usd", new Operation("op-stale-sold", At(2049, 5, 3, 9), -1_000_00, 840));
        bank.Put("stale-uah", new Operation("op-stale-client", At(2049, 5, 3, 9).AddSeconds(50), 40_100_00, 980));
        await using var app = Create(At(2049, 5, 5, 10), bank, Nbu(("USD", new DateOnly(2049, 5, 3), "40.2000")));
        using var owner = await Connect(app, _ownerEmail, "token-stale-sale");
        await Sync(app, owner);
        await Sync(app, owner);
        Assert.Equal(TransactionKind.FxSale, Assert.Single((await List(owner, 2049)).Items).Kind);

        bank.Put("stale-uah", new Operation("op-stale-sale", At(2049, 5, 3, 9).AddSeconds(2), 40_000_00, 980));
        await Sync(app, owner);
        await Sync(app, owner);

        var rows = (await List(owner, 2049)).Items.ToDictionary(row => row.AmountMinor);
        Assert.Equal((TransactionKind.FxSale, "monobank: currency sale"), (rows[40_000_00].Kind, rows[40_000_00].NonIncomeReason));
        Assert.Equal((TransactionKind.Income, (string?)null), (rows[40_100_00].Kind, rows[40_100_00].NonIncomeReason));
    }

    // Transaction keeps no counterparty IBAN, so a leg that loses its sale guess cannot be told from
    // any other income: it returns to Income although it named the owner's own account. Rule 12 says so.
    [Fact]
    public async Task A_displaced_sale_guess_that_named_an_own_account_returns_to_income()
    {
        var bank = new FakeBank();
        bank.Connect("token-displaced", ("disp-uah", 980), ("disp-usd", 840));
        bank.Put("disp-usd", new Operation("op-disp-sold", At(2030, 5, 3, 9), -1_000_00, 840));
        bank.Put("disp-uah", new Operation(
            "op-disp-own", At(2030, 5, 3, 9).AddSeconds(50), 40_100_00, 980, CounterName: "Me", CounterIban: "UAdisp-usd"));
        await using var app = Create(At(2030, 5, 5, 10), bank, Nbu(("USD", new DateOnly(2030, 5, 3), "40.2000")));
        using var owner = await Connect(app, _ownerEmail, "token-displaced");
        await Sync(app, owner);
        Assert.Equal(TransactionKind.FxSale, Assert.Single((await List(owner, 2030)).Items).Kind);

        bank.Put("disp-uah", new Operation("op-disp-sale", At(2030, 5, 3, 9).AddSeconds(2), 40_000_00, 980));
        await Sync(app, owner);

        var rows = (await List(owner, 2030)).Items.ToDictionary(row => row.AmountMinor);
        Assert.Equal(TransactionKind.FxSale, rows[40_000_00].Kind);
        Assert.Equal((TransactionKind.Income, (string?)null), (rows[40_100_00].Kind, rows[40_100_00].NonIncomeReason));
    }

    // A window ends where the next begins, so the hryvnia legs either side of the edge are read in
    // different windows. The one that settled closer to the debit has to win it, whichever is read first.
    [Fact]
    public async Task Two_legs_either_side_of_a_window_edge_do_not_both_claim_one_debit()
    {
        var registered = new DateOnly(2031, 1, 1);
        var edge = registered.KyivMidnight() + MonobankStatementImport.Window;
        var bank = new FakeBank();
        bank.Connect("token-edge", ("edge-usd", 840));
        bank.Put("edge-usd", new Operation("op-edge-sold", edge.AddSeconds(-30), -1_000_00, 840));
        await using var app = Create(At(2031, 3, 1, 10), bank, Nbu(("USD", new DateOnly(2031, 1, 31), "40.0000")));
        using var owner = await Connect(app, _otherEmail, "token-edge", registered);
        await DrainUntil(app, owner, "edge-usd", account => account.BackfillComplete && !account.SyncPending);

        bank.Connect("token-edge", ("edge-usd", 840), ("edge-uah", 980));
        bank.Put("edge-uah", new Operation("op-edge-early", edge.AddSeconds(-90), 40_000_00, 980, CounterName: "Early"));
        bank.Put("edge-uah", new Operation("op-edge-late", edge.AddSeconds(10), 40_000_00, 980, CounterName: "Late"));
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-edge" })).StatusCode);
        await DrainUntil(app, owner, "edge-uah", account => account.BackfillComplete && !account.SyncPending);

        var rows = (await List(owner, 2031)).Items.ToDictionary(row => row.ClientName!, row => row.Kind);
        Assert.Equal(TransactionKind.FxSale, rows["Late"]);
        Assert.Equal(TransactionKind.Income, rows["Early"]);
    }

    // The sync and restore take the owner's lock for whole windows, so NBU must be asked before it.
    [Fact]
    public async Task The_nbu_rates_a_sale_needs_are_fetched_before_the_owners_lock_is_taken()
    {
        var lockFreeWhileNbuAnswered = new ConcurrentQueue<bool>();
        var nbu = new StubNbuHandler(_ =>
        {
            using var scope = fixture.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerId = database.Users.Single(user => user.Email == _ownerEmail).Id;
            lockFreeWhileNbuAnswered.Enqueue(database.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtext({ownerId})) AS \"Value\"")
                .AsEnumerable()
                .Single());
            return StubNbuHandler.Json(StubNbuHandler.Row("USD", new DateOnly(2031, 6, 3), "40.0000"));
        });
        var bank = new FakeBank();
        bank.Connect("token-rates-first", ("rates-uah", 980), ("rates-usd", 840));
        bank.Put("rates-usd", new Operation("op-rates-sold", At(2031, 6, 3, 9), -1_000_00, 840));
        bank.Put("rates-uah", new Operation("op-rates-sale", At(2031, 6, 3, 9).AddSeconds(2), 40_000_00, 980));
        await using var app = Create(At(2031, 6, 5, 10), bank, nbu);
        using var owner = await Connect(app, _ownerEmail, "token-rates-first");
        await Sync(app, owner);

        Assert.Equal(TransactionKind.FxSale, Assert.Single((await List(owner, 2031)).Items).Kind);
        Assert.NotEmpty(lockFreeWhileNbuAnswered);
        Assert.All(lockFreeWhileNbuAnswered, free => Assert.True(free));
    }

    // The foreign account's whole backfill runs before the hryvnia account is even followed, so the
    // February sale is more than a window behind the foreign cursor when its hryvnia leg is read.
    [Fact]
    public async Task A_sale_pairs_when_the_foreign_account_was_walked_months_before_the_hryvnia_one()
    {
        var sale = At(2033, 2, 10, 9);
        var card = At(2033, 3, 7, 9);
        var bank = new FakeBank();
        bank.Connect("token-usd-first", ("first-usd", 840));
        bank.Put("first-usd", new Operation("op-first-sold", sale, -1_000_00, 840));
        bank.Put("first-usd", new Operation("op-first-card", card.AddSeconds(10), -25_00, 840));
        bank.Put("first-uah", new Operation("op-first-sale", sale.AddSeconds(3), 40_700_00, 980, CounterName: "monobank"));
        bank.Put("first-uah", new Operation("op-first-client", card, 300_00, 980, CounterName: "Client UA"));
        await using var app = Create(
            At(2033, 4, 20, 10),
            bank,
            Nbu(("USD", new DateOnly(2033, 2, 10), "41.0000"), ("USD", new DateOnly(2033, 3, 7), "41.2000")));
        using var owner = await Connect(app, _otherEmail, "token-usd-first", new DateOnly(2033, 1, 1));
        await DrainUntil(app, owner, "first-usd", account => account.BackfillComplete && !account.SyncPending);

        bank.Connect("token-usd-first", ("first-usd", 840), ("first-uah", 980));
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-usd-first" })).StatusCode);
        await DrainUntil(app, owner, "first-uah", account => account.BackfillComplete && !account.SyncPending);

        var rows = (await List(owner, 2033)).Items.ToDictionary(row => row.ClientName!, row => (row.Kind, row.NonIncomeReason));
        Assert.Equal((TransactionKind.FxSale, "monobank: currency sale"), rows["monobank"]);
        Assert.Equal((TransactionKind.Income, (string?)null), rows["Client UA"]);
    }

    [Fact]
    public async Task Confirming_a_kind_the_sync_has_since_changed_is_refused_and_saves_nothing()
    {
        var sale = At(2034, 7, 3, 9);
        var bank = new FakeBank();
        bank.Connect("token-stale-confirm", ("stale-confirm-uah", 980), ("stale-confirm-usd", 840));
        bank.Put("stale-confirm-uah", new Operation("op-stale-confirm-sale", sale.AddSeconds(2), 40_000_00, 980));
        await using var app = Create(At(2034, 7, 5, 10), bank, Nbu(("USD", new DateOnly(2034, 7, 3), "40.1000")));
        using var owner = await Connect(app, _otherEmail, "token-stale-confirm");
        var shown = Assert.Single((await List(owner, 2034)).Items);
        Assert.Equal(TransactionKind.Income, shown.Kind);

        bank.Put("stale-confirm-usd", new Operation("op-stale-confirm-sold", sale, -1_000_00, 840));
        await Sync(app, owner);

        var refused = await Confirm(owner, shown);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var row = Assert.Single((await List(owner, 2034)).Items);
        Assert.Equal((TransactionKind.FxSale, ReviewStatus.NeedsReview), (row.Kind, row.ReviewStatus));

        var confirmed = await Confirm(owner, row);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var after = Assert.Single((await List(owner, 2034)).Items);
        Assert.Equal((TransactionKind.FxSale, ReviewStatus.Confirmed), (after.Kind, after.ReviewStatus));
    }

    [Fact]
    public async Task Confirming_or_editing_clears_a_row_from_review_and_the_dashboard_warning_follows()
    {
        var bank = new FakeBank();
        bank.Connect("token-review", ("review-uah", 980));
        await using var app = Create(At(2046, 8, 5, 10), bank);
        using var owner = await Connect(app, _ownerEmail, "token-review");
        var waiting = await NeedsReviewCount(owner);
        bank.Put("review-uah", new Operation("op-keep", At(2046, 8, 1, 9), 100_00, 980, CounterName: "Keep"));
        bank.Put("review-uah", new Operation("op-change", At(2046, 8, 2, 9), 200_00, 980, CounterName: "Change"));

        await Sync(app, owner);
        Assert.Equal(waiting + 2, await NeedsReviewCount(owner));
        var rows = (await List(owner, 2046)).Items;
        var keep = rows.Single(row => row.ClientName == "Keep");
        var change = rows.Single(row => row.ClientName == "Change");

        var confirm = await Confirm(owner, keep);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmed = (await confirm.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal((ReviewStatus.Confirmed, TransactionKind.Income), (confirmed.ReviewStatus, confirmed.Kind));
        Assert.Equal(waiting + 1, await NeedsReviewCount(owner));

        var withoutReason = Edit(change, TransactionKind.OwnTransfer, reason: null);
        var rejected = await owner.PutAsJsonAsync($"/api/transactions/{change.Id}", withoutReason, Json);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains(await Review(owner), row => row.Id == change.Id);

        var reclassified = await owner.PutAsJsonAsync(
            $"/api/transactions/{change.Id}", Edit(change, TransactionKind.OwnTransfer, "From my personal card"), Json);
        Assert.Equal(HttpStatusCode.OK, reclassified.StatusCode);
        Assert.Equal(ReviewStatus.Confirmed, (await reclassified.Content.ReadFromJsonAsync<TransactionResponse>(Json))!.ReviewStatus);
        Assert.Equal(waiting, await NeedsReviewCount(owner));
        Assert.DoesNotContain(await Review(owner), row => row.ValueDate.Year == 2046);

        await Sync(app, owner);
        Assert.Equal(
            [(TransactionKind.Income, ReviewStatus.Confirmed), (TransactionKind.OwnTransfer, ReviewStatus.Confirmed)],
            (await List(owner, 2046)).Items.OrderBy(row => row.AmountMinor).Select(row => (row.Kind, row.ReviewStatus)));
    }

    [Fact]
    public async Task Another_owner_can_neither_see_nor_review_nor_dismiss_an_imported_row()
    {
        var bank = new FakeBank();
        bank.Connect("token-review-iso", ("review-iso-uah", 980));
        bank.Put("review-iso-uah", new Operation("op-review-iso", At(2048, 3, 1, 9), 100_00, 980));
        await using var app = Create(At(2048, 3, 2, 10), bank);
        using var owner = await Connect(app, _ownerEmail, "token-review-iso");
        await Sync(app, owner);
        var row = Assert.Single((await List(owner, 2048)).Items);
        using var stranger = await ApiFixture.SignIn(app.Factory, _otherEmail);

        Assert.DoesNotContain(await Review(stranger), each => each.Id == row.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirm(stranger, row)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/transactions/{row.Id}")).StatusCode);

        Assert.Equal(ReviewStatus.NeedsReview, Assert.Single((await List(owner, 2048)).Items).ReviewStatus);
        Assert.Contains(await Review(owner), each => each.Id == row.Id);
    }

    // It restores the owner's whole backup, and a restore rejects a row dated after this test's today.
    // The class shares its owners, and other tests leave this one rows in later years, so the test
    // first restores the owner's own file with no transactions.
    [Fact]
    public async Task A_deleted_import_stays_dismissed_through_syncs_and_a_restore_and_counts_nowhere_while_a_manual_row_is_deleted()
    {
        const int year = 2051;
        var bank = new FakeBank();
        bank.Connect("token-tombstone", ("tomb-uah", 980));
        bank.Put("tomb-uah", new Operation("op-tomb", At(year, 6, 1, 9), 500_00, 980, Description: "Dismissed receipt"));
        await using var app = Create(At(year, 6, 10, 10), bank);
        await EmptyLedger(app, _otherEmail);
        using var owner = await Connect(app, _otherEmail, "token-tombstone");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", TaxYear())).StatusCode);
        var settings = new SettingsRequest(
            new DateOnly(year, 1, 1), PaymentMode.Quarterly, EsvRegistrationMonthPolicy.FullMonth, false, true, true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday], "uk", "system", "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", settings, Json)).StatusCode);
        var manual = await Post(owner, new TransactionRequest(
            new DateOnly(year, 6, 2), 100_00, Currency.UAH, null, TransactionKind.Income, null, null, null, "Kept receipt", null));
        var gone = await Post(owner, new TransactionRequest(
            new DateOnly(year, 6, 3), 70_00, Currency.UAH, null, TransactionKind.Income, null, null, null, "Deleted receipt", null));
        await Sync(app, owner);
        var imported = (await List(owner, year)).Items.Single(row => row.Source is not null);
        var waiting = await NeedsReviewCount(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{imported.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{gone.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/transactions/{imported.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirm(owner, imported)).StatusCode);
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));

        await Sync(app, owner);
        Assert.Equal((0, 0), Counts(await Status(owner), "tomb-uah"));
        await AssertOnlyTheManualRowCounts(owner, year, manual.Id);

        var file = await owner.GetStringAsync("/api/backup");
        var stored = Parse(file).GetProperty("transactions").EnumerateArray().ToArray();
        Assert.Equal("Dismissed", stored.Single(row => row.GetProperty("id").GetGuid() == imported.Id).GetProperty("reviewStatus").GetString());
        Assert.DoesNotContain(stored, row => row.GetProperty("id").GetGuid() == gone.Id);

        var restore = await owner.PostAsync("/api/restore", new StringContent(file, System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-tombstone" })).StatusCode);
        await Sync(app, owner);

        Assert.Equal((0, 0), Counts(await Status(owner), "tomb-uah"));
        await AssertOnlyTheManualRowCounts(owner, year, manual.Id);
    }

    [Fact]
    public async Task A_client_only_a_dismissed_import_points_at_can_be_deleted_and_the_tombstone_forgets_it()
    {
        const int year = 2078;
        var bank = new FakeBank();
        bank.Connect("token-dismissed-client", ("dismissed-client-uah", 980));
        bank.Put("dismissed-client-uah", new Operation(
            "op-dismissed-client", At(year, 6, 1, 9), 500_00, 980, CounterName: "Dismissed Buyer"));
        await using var app = Create(At(year, 6, 10, 10), bank);
        await EmptyLedger(app, _otherEmail);
        using var owner = await Connect(app, _otherEmail, "token-dismissed-client");
        var imported = Assert.Single((await List(owner, year)).Items);
        var clients = await owner.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json);
        var client = Assert.Single(clients!, row => row.Name == "Dismissed Buyer");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/clients/{client.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{imported.Id}")).StatusCode);

        clients = await owner.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json);
        Assert.Equal(0, Assert.Single(clients!, row => row.Id == client.Id).ReceiptCount);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/clients/{client.Id}")).StatusCode);

        var stored = Parse(await owner.GetStringAsync("/api/backup")).GetProperty("transactions").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == imported.Id);
        Assert.Equal("Dismissed", stored.GetProperty("reviewStatus").GetString());
        Assert.Equal("op-dismissed-client", stored.GetProperty("externalId").GetString());
        Assert.Equal(JsonValueKind.Null, stored.GetProperty("clientId").ValueKind);
    }

    private static async Task EmptyLedger(SyncApp app, string email)
    {
        using var owner = await ApiFixture.SignIn(app.Factory, email);
        var file = System.Text.Json.Nodes.JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        file["transactions"] = new System.Text.Json.Nodes.JsonArray();
        var restore = await owner.PostAsync(
            "/api/restore", new StringContent(file.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
    }

    private static async Task AssertOnlyTheManualRowCounts(HttpClient owner, int year, Guid manualId)
    {
        var list = await List(owner, year);
        Assert.Equal((manualId, 100_00L), (Assert.Single(list.Items).Id, list.TotalIncomeKop));
        Assert.DoesNotContain(await Review(owner), row => row.ValueDate.Year == year);

        var periods = await owner.GetFromJsonAsync<JsonElement>($"/api/periods/{year}", Json);
        Assert.Equal(100_00, periods.GetProperty("quarters").EnumerateArray().Sum(quarter => quarter.GetProperty("incomeKop").GetInt64()));

        var dashboard = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard", Json);
        Assert.Equal(100_00, dashboard.GetProperty("limit").GetProperty("incomeKop").GetInt64());

        var csv = await owner.GetStringAsync($"/api/export/transactions.csv?year={year}");
        Assert.Contains("Kept receipt", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Dismissed receipt", csv, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient owner, TransactionResponse shown) =>
        owner.PostAsJsonAsync($"/api/transactions/{shown.Id}/confirm", new { kind = shown.Kind }, Json);

    private static TransactionRequest Edit(TransactionResponse row, TransactionKind kind, string? reason) =>
        new(row.ValueDate, row.AmountMinor, row.Currency, null, kind, reason, row.ClientName, row.InvoiceNumber, row.Description, null);

    private static TaxYearConfigRequest TaxYear() =>
        new(800_000L, 600, 200, 2100, 1600, 1200, [80, 95], 20, 41, 11, 16, 10, [], "a test source");

    private static async Task<TransactionResponse[]> Review(HttpClient owner) =>
        (await owner.GetFromJsonAsync<TransactionResponse[]>("/api/transactions/review", Json))!;

    private static async Task<int> NeedsReviewCount(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonElement>("/api/dashboard", Json)).GetProperty("needsReviewCount").GetInt32();
}
