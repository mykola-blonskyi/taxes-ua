using System.Net;
using System.Net.Http.Json;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Monobank;

// The invoicing prefill reads client-info through the same reader as the token save and the jar reads (#116, #148).
public sealed partial class MonobankSyncTests
{
    private const string PrefillUrl = "/api/settings/invoicing/prefill-from-monobank";

    [Fact]
    public async Task A_prefill_right_after_connecting_answers_from_the_token_saves_answer_without_a_bank_call()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-wait", ("prefill-wait-fop", 980));
        await using var app = Create(At(2095, 6, 10, 10), bank);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-wait");
        var spent = bank.ClientInfoCalls(app.Handler).Length;
        app.Clock.Advance(TimeSpan.FromSeconds(20));

        // A request that parked on the gate would never return here: the fake clock does not move.
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var response = await owner.PostAsync(PrefillUrl, null, patience.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Test FOP", (await response.Content.ReadFromJsonAsync<MonobankPrefillResponse>(Json))!.SellerNameUk);
        Assert.Equal(1, spent);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);
    }

    [Fact]
    public async Task A_prefill_within_a_minute_of_a_jar_refresh_shares_that_answer()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-twice", ("prefill-twice-fop", 980));
        bank.Jars("token-prefill-twice", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 6, 11, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-twice");
        await Choose(owner, TaxesJar);
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await Refresh(owner)).StatusCode);
        var spent = bank.ClientInfoCalls(app.Handler).Length;
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var again = await owner.PostAsync(PrefillUrl, null, patience.Token);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);

        await ForgetJar(owner);
    }

    [Fact]
    public async Task No_path_calls_client_info_twice_within_a_minute_after_the_token_save()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-paths", ("prefill-paths-fop", 980));
        bank.Jars("token-prefill-paths", (TaxesJar, "На податки", 980, 10_000_00));
        await using var app = Create(At(2095, 6, 14, 10), bank);
        await ClearJars(app);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-paths");
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // The token save's minute: every path is served from its answer.
        await Ok(owner.PostAsync(PrefillUrl, null, patience.Token));
        await Ok(owner.GetAsync("/api/monobank/jars", patience.Token));
        await Choose(owner, TaxesJar);
        await Ok(Refresh(owner));
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        await Ok(owner.PostAsync(PrefillUrl, null, patience.Token));
        Assert.Single(bank.ClientInfoCalls(app.Handler));

        // The next minute: whichever path reads first makes the one call, and the others share it.
        app.Clock.Advance(TimeSpan.FromSeconds(31));
        await Ok(owner.PostAsync(PrefillUrl, null, patience.Token));
        await Ok(Refresh(owner));
        await Ok(owner.GetAsync("/api/monobank/jars", patience.Token));
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        await Ok(owner.PostAsync(PrefillUrl, null, patience.Token));
        app.Clock.Advance(TimeSpan.FromSeconds(31));
        await Ok(Refresh(owner));

        var calls = bank.ClientInfoCalls(app.Handler);
        Assert.Equal(3, calls.Length);
        Assert.All(calls.Zip(calls.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= MonobankRateGate.Interval));

        await ForgetJar(owner);
    }

    [Fact]
    public async Task A_prefill_asks_to_wait_when_the_slot_is_spent_and_no_answer_is_held()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-spent", ("prefill-spent-fop", 980));
        await using var app = Create(At(2095, 6, 13, 10), bank);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-spent");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        // A token save the bank refuses still spends the slot, and the answer held for the old token is over.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-nobody" })).StatusCode);
        var spent = bank.ClientInfoCalls(app.Handler).Length;
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var response = await owner.PostAsync(PrefillUrl, null, patience.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(MonobankRateGate.Interval, response.Headers.RetryAfter!.Delta);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);
    }

    [Fact]
    public async Task A_prefill_in_flight_when_the_token_is_replaced_is_discarded_and_never_cached()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-race-a", ("prefill-race-fop", 980));
        bank.Name("token-prefill-race-a", "Old FOP");
        bank.Connect("token-prefill-race-b", ("prefill-race-fop", 980));
        bank.Name("token-prefill-race-b", "New FOP");
        await using var app = Create(At(2095, 6, 15, 10), bank);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-race-a");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.HoldClientInfo("token-prefill-race-a");

        var inFlight = owner.PostAsync(PrefillUrl, null);
        Assert.True(bank.HeldArrived(TimeSpan.FromSeconds(10)));
        var replaced = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-prefill-race-b" });
        bank.ReleaseClientInfo();
        var late = await inFlight;
        var spent = bank.ClientInfoCalls(app.Handler).Length;
        var next = await owner.PostAsync(PrefillUrl, null);

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, late.StatusCode);
        Assert.DoesNotContain("Old FOP", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal("New FOP", (await next.Content.ReadFromJsonAsync<MonobankPrefillResponse>(Json))!.SellerNameUk);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);
    }

    [Fact]
    public async Task A_token_monobank_rejects_on_prefill_is_a_conflict_so_the_owner_connects_again()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-rejected", ("prefill-rejected-fop", 980));
        await using var app = Create(At(2095, 6, 12, 10), bank);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-rejected");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        bank.Revoke("token-prefill-rejected");

        var response = await owner.PostAsync(PrefillUrl, null);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Connect monobank again", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token-prefill-rejected", body, StringComparison.Ordinal);
    }

    private static async Task Ok(Task<HttpResponseMessage> request) =>
        Assert.Equal(HttpStatusCode.OK, (await request).StatusCode);
}
