using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class MonobankRateGateTests
{
    [Fact]
    public async Task A_wait_that_overruns_its_slot_still_keeps_the_next_call_a_full_interval_away()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var gate = new MonobankRateGate(clock);
        await gate.WaitTurnAsync("owner", "statement", CancellationToken.None);

        var second = gate.WaitTurnAsync("owner", "statement", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(15));
        await second;
        var secondCall = clock.GetUtcNow();

        var third = gate.WaitTurnAsync("owner", "statement", CancellationToken.None);
        clock.Advance(MonobankRateGate.Interval - TimeSpan.FromSeconds(1));
        Assert.False(third.IsCompleted, "the third call must wait a full interval after the late second call");
        clock.Advance(TimeSpan.FromSeconds(1));
        await third;
        Assert.Equal(secondCall + MonobankRateGate.Interval, clock.GetUtcNow());
    }
}
