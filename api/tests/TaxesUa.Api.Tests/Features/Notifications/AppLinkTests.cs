using Microsoft.Extensions.Configuration;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class AppLinkTests
{
    [Theory]
    [InlineData("https://taxes.example", "taxes.other", "https://taxes.example/")]
    [InlineData(" https://taxes.example/app/ ", null, "https://taxes.example/app/")]
    [InlineData("", "taxes.example;www.taxes.example", "https://taxes.example/")]
    [InlineData(null, "*", null)]
    [InlineData(null, "*.taxes.example", null)]
    [InlineData(null, null, null)]
    public void The_link_is_the_public_url_or_else_the_first_pinned_domain(string? publicUrl, string? allowedHosts, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicUrl"] = publicUrl, ["AllowedHosts"] = allowedHosts })
            .Build();

        Assert.Equal(expected, new AppLink(configuration).Url);
    }
}
