using System.Net;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

// The invoicing prefill shares the client-info slot with the token save and the jar reads (#116).
public sealed partial class MonobankSyncTests
{
    private const string PrefillUrl = "/api/settings/invoicing/prefill-from-monobank";

    [Fact]
    public async Task A_prefill_right_after_connecting_answers_at_once_with_how_long_to_wait()
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

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(40), response.Headers.RetryAfter!.Delta);
        Assert.Equal(spent, bank.ClientInfoCalls(app.Handler).Length);

        app.Clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(PrefillUrl, null, patience.Token)).StatusCode);
        Assert.Equal(spent + 1, bank.ClientInfoCalls(app.Handler).Length);
    }

    [Fact]
    public async Task A_second_prefill_within_the_minute_asks_to_wait_instead_of_calling_the_bank()
    {
        var bank = new FakeBank();
        bank.Connect("token-prefill-twice", ("prefill-twice-fop", 980));
        await using var app = Create(At(2095, 6, 11, 10), bank);
        using var owner = await ConnectAtOnce(app, ApiFixture.AllowedEmail, "token-prefill-twice");
        app.Clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(PrefillUrl, null, patience.Token)).StatusCode);
        var spent = bank.ClientInfoCalls(app.Handler).Length;

        var again = await owner.PostAsync(PrefillUrl, null, patience.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.Equal(MonobankRateGate.Interval, again.Headers.RetryAfter!.Delta);
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

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Connect monobank again", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
