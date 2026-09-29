using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class MonobankNightlySyncTests
{
    [Theory]
    [InlineData("2078-06-10T10:00:00Z", "2078-06-11T00:00:00Z")]
    [InlineData("2078-06-10T23:59:59Z", "2078-06-11T00:00:00Z")]
    [InlineData("2078-06-11T00:00:00Z", "2078-06-12T00:00:00Z")]
    [InlineData("2079-01-10T00:30:00Z", "2079-01-10T01:00:00Z")]
    [InlineData("2079-01-10T01:00:00Z", "2079-01-11T01:00:00Z")]
    [InlineData("2079-03-26T00:30:00Z", "2079-03-26T01:00:00Z")]
    [InlineData("2079-10-29T00:30:00Z", "2079-10-29T01:00:00Z")]
    public void The_next_run_is_the_next_three_oclock_in_Kyiv(string now, string next) =>
        Assert.Equal(DateTimeOffset.Parse(next), MonobankNightlySync.NextRun(DateTimeOffset.Parse(now)));
}
