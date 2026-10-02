using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

// The reserve jar (#102). The owners are shared by the whole class and the jar is one row per owner, so each
// test starts its owner with no jar and leaves none behind.
public sealed partial class MonobankSyncTests
{
    private const string TaxesJar = "jar-taxes";

    [Fact]
    public async Task Only_hryvnia_jars_are_offered_and_the_choice_is_stored_with_the_time_it_was_read()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-list", ("jar-list-fop", 980));
        bank.Jars("token-jar-list", (TaxesJar, "На податки", 980, 12_345_00), ("jar-usd", "Dollars", 840, 50_00),
            ("jar-eur", "Euros", 978, 1_00), ("jar-trip", "Подорож", 980, 0));
        await using var app = Create(At(2095, 3, 10, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-list");
        await ForgetJar(owner);

        var offered = (await owner.GetFromJsonAsync<JarChoicesResponse>("/api/monobank/jars", Json))!;
        var foreign = await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = "jar-usd" }, Json);
        var unknown = await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = "jar-nobody" }, Json);
        var blank = await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = " " }, Json);
        var chosen = await Choose(owner, TaxesJar);

        Assert.Equal([(TaxesJar, "На податки", 12_345_00L), ("jar-trip", "Подорож", 0L)], offered.Jars.Select(jar => (jar.Id, jar.Title, jar.BalanceKop)));
        Assert.All([foreign, unknown, blank], response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        var read = Assert.Single(bank.ClientInfoCalls(app.Handler)).At;
        Assert.Equal((TaxesJar, "На податки", 12_345_00L, read, false), (chosen.JarId, chosen.Title, chosen.BalanceKop, chosen.FetchedAt, chosen.Stale));
        Assert.Equal(chosen, (await StoredJar(owner)).Jar);

        await ForgetJar(owner);
        Assert.Null((await StoredJar(owner)).Jar);
    }

    [Fact]
    public async Task Listing_choosing_and_refreshing_never_call_client_info_twice_within_a_minute()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-gate", ("jar-gate-fop", 980));
        bank.Jars("token-jar-gate", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 4, 10, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-gate");
        await ForgetJar(owner);

        // The token save made the one call; everything in the minute after it is served from its answer.
        await owner.GetAsync("/api/monobank/jars");
        await Choose(owner, TaxesJar);
        bank.Jars("token-jar-gate", (TaxesJar, "На податки", 980, 20_000_00));
        var tooSoon = await Refresh(owner);
        Assert.Single(bank.ClientInfoCalls(app.Handler));
        Assert.Equal(10_000_00, (await tooSoon.Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!.BalanceKop);

        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        var fresh = (await (await Refresh(owner)).Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!;
        bank.Jars("token-jar-gate", (TaxesJar, "На податки", 980, 30_000_00));
        var again = (await (await Refresh(owner)).Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!;

        var calls = bank.ClientInfoCalls(app.Handler);
        Assert.Equal(2, calls.Length);
        Assert.Equal((20_000_00L, calls[1].At), (fresh.BalanceKop, fresh.FetchedAt));
        Assert.Equal(fresh, again);
        Assert.All(calls.Zip(calls.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= MonobankRateGate.Interval));

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_refresh_within_a_minute_of_the_invoicing_prefill_shares_its_answer()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-wait", ("jar-wait-fop", 980));
        bank.Jars("token-jar-wait", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 5, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-wait");
        await ForgetJar(owner);
        await Choose(owner, TaxesJar);
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.Jars("token-jar-wait", (TaxesJar, "На податки", 980, 15_000_00));
        var prefill = await owner.PostAsync("/api/settings/invoicing/prefill-from-monobank", null);
        Assert.Equal(HttpStatusCode.OK, prefill.StatusCode);
        var spent = bank.ClientInfoCalls(app.Handler).Length;

        var response = await Refresh(owner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(15_000_00, (await response.Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!.BalanceKop);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_sync_run_refreshes_the_chosen_jars_balance_through_the_gate()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-sync", ("jar-sync-fop", 980));
        bank.Jars("token-jar-sync", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 6, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-sync");
        await ForgetJar(owner);
        await Choose(owner, TaxesJar);
        var before = bank.ClientInfoCalls(app.Handler).Length;

        // A minute on, so the answer the choice used is over and the gate's slot is free.
        bank.Jars("token-jar-sync", (TaxesJar, "На податки", 980, 11_000_00));
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        await Sync(app, owner);
        var first = (await StoredJar(owner)).Jar!;
        bank.Jars("token-jar-sync", (TaxesJar, "На податки", 980, 12_000_00));
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        await Sync(app, owner);

        var second = (await StoredJar(owner)).Jar!;
        var calls = bank.ClientInfoCalls(app.Handler);
        Assert.Equal(before + 2, calls.Length);
        Assert.Equal((11_000_00L, calls[before].At), (first.BalanceKop, first.FetchedAt));
        Assert.Equal((12_000_00L, calls[before + 1].At), (second.BalanceKop, second.FetchedAt));
        Assert.All(calls.Zip(calls.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= MonobankRateGate.Interval));

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_sync_without_a_chosen_jar_does_not_ask_the_bank_for_jars()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-none", ("jar-none-fop", 980));
        await using var app = Create(At(2095, 7, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-none");
        await ForgetJar(owner);

        app.Clock.Advance(MonobankRateGate.Interval * 2);
        await Sync(app, owner);

        Assert.Single(bank.ClientInfoCalls(app.Handler));
    }

    [Fact]
    public async Task A_balance_the_bank_cannot_refresh_keeps_its_time_and_turns_stale_after_a_day()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-stale", ("jar-stale-fop", 980));
        bank.Jars("token-jar-stale", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 8, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-stale");
        await ForgetJar(owner);
        // Written straight to the row: moving the fake clock a day would run the nightly sync as well.
        var fetchedAt = app.Clock.GetUtcNow() - TimeSpan.FromHours(23);
        await StoreJar(app, _ownerEmail, fetchedAt);
        var fresh = (await StoredJar(owner)).Jar!;
        fetchedAt -= TimeSpan.FromHours(2);
        await StoreJar(app, _ownerEmail, fetchedAt);
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.ClientInfoFails = true;
        var failed = await Refresh(owner);
        var stale = (await StoredJar(owner)).Jar!;
        bank.ClientInfoFails = false;
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        var recovered = (await (await Refresh(owner)).Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!;

        Assert.False(fresh.Stale);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal((1_000_00L, fetchedAt, true), (stale.BalanceKop, stale.FetchedAt, stale.Stale));
        Assert.Equal((10_000_00L, false, app.Clock.GetUtcNow()), (recovered.BalanceKop, recovered.Stale, recovered.FetchedAt));

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_jar_the_bank_no_longer_reports_is_kept_with_its_time_and_a_refresh_says_so()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-gone", ("jar-gone-fop", 980));
        bank.Jars("token-jar-gone", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 9, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-gone");
        await ForgetJar(owner);
        var chosen = await Choose(owner, TaxesJar);
        bank.Jars("token-jar-gone");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));

        var refresh = await Refresh(owner);

        Assert.Equal(HttpStatusCode.Conflict, refresh.StatusCode);
        Assert.Equal(chosen, (await StoredJar(owner)).Jar);

        await ForgetJar(owner);
    }

    [Fact]
    public async Task Refreshing_without_a_chosen_jar_or_without_a_connection_is_a_conflict()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-nothing", ("jar-nothing-fop", 980));
        await using var app = Create(At(2095, 10, 10, 10), bank);
        await ClearJars(app);
        using var owner = await Connect(app, _ownerEmail, "token-jar-nothing");
        await ForgetJar(owner);

        var noJar = await Refresh(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        var noConnection = await owner.GetAsync("/api/monobank/jars");

        Assert.Equal(HttpStatusCode.Conflict, noJar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noConnection.StatusCode);
    }

    [Fact]
    public async Task A_jar_and_its_balance_belong_to_their_owner_alone()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-first", ("jar-first-fop", 980));
        bank.Jars("token-jar-first", (TaxesJar, "Перша скарбничка", 980, 12_345_00));
        bank.Connect("token-jar-second", ("jar-second-fop", 980));
        bank.Jars("token-jar-second", ("jar-second", "Друга скарбничка", 980, 777_00));
        await using var app = Create(At(2095, 11, 10, 10), bank);
        await ClearJars(app);
        using var first = await Connect(app, _ownerEmail, "token-jar-first");
        using var second = await Connect(app, _otherEmail, "token-jar-second");
        await ForgetJar(first);
        await ForgetJar(second);
        await Choose(first, TaxesJar);

        var seenBySecond = await second.GetStringAsync("/api/monobank/reserve-jar");
        var listedForSecond = await second.GetStringAsync("/api/monobank/jars");
        var stealing = await second.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = TaxesJar }, Json);
        var refreshingNothing = await Refresh(second);
        var own = await Choose(second, "jar-second");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.Jars("token-jar-first", (TaxesJar, "Перша скарбничка", 980, 5_00));
        bank.Jars("token-jar-second", ("jar-second", "Друга скарбничка", 980, 888_00));
        await Refresh(second);
        await second.DeleteAsync("/api/monobank/reserve-jar");

        Assert.Null((await StoredJar(second)).Jar);
        Assert.DoesNotContain("Перша", seenBySecond + listedForSecond, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", seenBySecond + listedForSecond, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, stealing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refreshingNothing.StatusCode);
        Assert.Equal(("jar-second", 777_00L), (own.JarId, own.BalanceKop));
        var kept = (await StoredJar(first)).Jar!;
        Assert.Equal(("Перша скарбничка", 12_345_00L), (kept.Title, kept.BalanceKop));

        await ForgetJar(first);
    }

    [Fact]
    public async Task The_jar_endpoints_need_a_session_and_never_return_the_token()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-secret", ("jar-secret-fop", 980));
        bank.Jars("token-jar-secret", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 12, 10, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-secret");
        await ForgetJar(owner);
        using var visitor = ApiFixture.CreateClient(app.Factory);

        var bodies = new List<string>
        {
            await owner.GetStringAsync("/api/monobank/jars"),
            await (await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = TaxesJar }, Json)).Content.ReadAsStringAsync(),
            await owner.GetStringAsync("/api/monobank/reserve-jar"),
            await (await Refresh(owner)).Content.ReadAsStringAsync(),
            await (await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = "jar-nobody" }, Json)).Content.ReadAsStringAsync(),
        };

        Assert.All(bodies, body => Assert.DoesNotContain("token-jar-secret", body, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/monobank/jars")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/monobank/reserve-jar")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await visitor.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = TaxesJar }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.DeleteAsync("/api/monobank/reserve-jar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync("/api/monobank/reserve-jar/refresh", null)).StatusCode);

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_read_in_flight_when_the_token_is_replaced_is_discarded_and_never_cached()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-race-a", ("jar-race-fop", 980));
        bank.Jars("token-jar-race-a", ("jar-old", "Стара скарбничка", 980, 1_00));
        bank.Connect("token-jar-race-b", ("jar-race-fop", 980));
        bank.Jars("token-jar-race-b", ("jar-new", "Нова скарбничка", 980, 2_00));
        await using var app = Create(At(2095, 2, 10, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-race-a");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.HoldClientInfo("token-jar-race-a");

        var inFlight = owner.GetAsync("/api/monobank/jars");
        Assert.True(bank.HeldArrived(TimeSpan.FromSeconds(10)));
        var replaced = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-jar-race-b" });
        bank.ReleaseClientInfo();
        var late = await inFlight;
        var listed = (await owner.GetFromJsonAsync<JarChoicesResponse>("/api/monobank/jars", Json))!;

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, late.StatusCode);
        Assert.DoesNotContain("Стара", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(["jar-new"], listed.Jars.Select(jar => jar.Id));
    }

    [Fact]
    public async Task A_token_save_spends_the_slot_even_when_the_bank_refuses_the_token()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-spent", ("jar-spent-fop", 980));
        bank.Jars("token-jar-spent", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 1, 10, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-spent");
        // The connecting sync refreshes the jar after its statement. Still running when the clock moves, it would
        // take the freed slot and the refresh below would be served its answer, so it finishes before a jar exists.
        await Quiet(app);
        await Choose(owner, TaxesJar);
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));

        var refused = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-nobody" });
        var calls = bank.ClientInfoCalls(app.Handler).Length;
        var refresh = await Refresh(owner);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refresh.StatusCode);
        Assert.Equal(calls, bank.ClientInfoCalls(app.Handler).Length);

        await ForgetJar(owner);
    }

    [Fact]
    public async Task Choosing_twice_at_once_leaves_one_jar_and_no_error()
    {
        var bank = new FakeBank();
        bank.Connect("token-jar-double", ("jar-double-fop", 980));
        bank.Jars("token-jar-double", (TaxesJar, "На податки", 980, 10_000_00), ("jar-trip", "Подорож", 980, 5_00));
        await using var app = Create(At(2095, 3, 20, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, _ownerEmail, "token-jar-double");

        var both = await Task.WhenAll(
            owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = TaxesJar }, Json),
            owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId = "jar-trip" }, Json));

        Assert.All(both, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Contains((await StoredJar(owner)).Jar!.JarId, new[] { TaxesJar, "jar-trip" });

        await ForgetJar(owner);
    }

    private static async Task<ReserveJarResponse> Choose(HttpClient owner, string jarId)
    {
        var response = await owner.PutAsJsonAsync("/api/monobank/reserve-jar", new { jarId }, Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReserveJarResponse>(Json))!;
    }

    // Without waiting for the connecting sync, which would move the fake clock a minute while the statement
    // gate is busy: the token save's answer then stays the one a read within the minute reuses.
    private static Task<HttpClient> ConnectAtOnce(SyncApp app, string email, string token) =>
        Connect(app, email, token, app.Clock.GetUtcNow().KyivDate());

    // Before the owner connects: a jar an earlier test left behind would be refreshed by the connecting sync,
    // and its call would count against this test's.
    private static async Task ClearJars(SyncApp app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReserveJars.ExecuteDeleteAsync();
    }

    // The balance is 1,000.00 and the jar the one the fake bank reports, so a refresh visibly changes it.
    private static async Task StoreJar(SyncApp app, string email, DateTimeOffset fetchedAt)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.ReserveJars.Where(row => row.UserId == user!.Id).ExecuteDeleteAsync();
        database.ReserveJars.Add(new ReserveJar
        {
            UserId = user!.Id,
            JarId = TaxesJar,
            Title = "На податки",
            BalanceKop = 1_000_00,
            FetchedAt = fetchedAt,
        });
        await database.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient owner) => owner.PostAsync("/api/monobank/reserve-jar/refresh", null);

    private static async Task<ReserveJarStateResponse> StoredJar(HttpClient owner) =>
        (await owner.GetFromJsonAsync<ReserveJarStateResponse>("/api/monobank/reserve-jar", Json))!;

    private static async Task ForgetJar(HttpClient owner) =>
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/reserve-jar")).StatusCode);
}
