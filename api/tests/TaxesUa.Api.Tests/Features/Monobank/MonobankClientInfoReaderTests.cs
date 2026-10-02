using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class MonobankClientInfoReaderTests
{
    [Fact]
    public async Task A_read_whose_token_changed_before_it_began_is_refused_without_asking_the_bank()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var gate = new MonobankRateGate(clock);
        var reader = new MonobankClientInfoReader(gate, clock);
        var generation = reader.Generation("owner");

        // The token is saved again after the caller loaded the old one and before it asks for the answer.
        reader.Remember("owner", new MonobankClientInfo("client", "New FOP", [], []));
        var read = await reader.ReadAsync(null!, "owner", "old-token", generation, CancellationToken.None);

        Assert.IsType<ClientInfoRead.Unavailable>(read);
        var current = await reader.ReadAsync(null!, "owner", "new-token", reader.Generation("owner"), CancellationToken.None);
        Assert.Equal("New FOP", Assert.IsType<ClientInfoRead.Found>(current).Name);
    }
}
