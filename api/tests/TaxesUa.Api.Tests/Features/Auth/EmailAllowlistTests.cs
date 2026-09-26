using Microsoft.Extensions.Configuration;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class EmailAllowlistTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",;,")]
    public void An_unconfigured_allowlist_permits_nothing(string? configured) =>
        Assert.False(Allowlist(configured).Permits("owner@example.com"));

    [Theory]
    [InlineData("owner@example.com", "owner@example.com", true)]
    [InlineData(" owner@example.com ; second@example.com", "second@example.com", true)]
    [InlineData("owner@example.com,second@example.com", "OWNER@Example.COM", true)]
    [InlineData("owner@example.com", "stranger@example.com", false)]
    [InlineData("owner@example.com", null, false)]
    public void Only_a_listed_address_is_permitted(string configured, string? candidate, bool expected) =>
        Assert.Equal(expected, Allowlist(configured).Permits(candidate));

    private static EmailAllowlist Allowlist(string? configured) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:AllowedEmails"] = configured })
            .Build());
}
