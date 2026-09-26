using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace TaxesUa.Api.Tests;

public sealed class StartupTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("taxes.example;*")]
    public void Production_refuses_to_start_without_a_pinned_host(string allowedHosts)
    {
        using var application = fixture.CreateApplication(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("AllowedHosts", allowedHosts);
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("ALLOWED_HOSTS", failure.ToString(), StringComparison.Ordinal);
    }
}
