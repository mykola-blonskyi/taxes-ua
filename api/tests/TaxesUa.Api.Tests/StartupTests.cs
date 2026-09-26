using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace TaxesUa.Api.Tests;

public sealed class StartupTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string DeployedHost = "taxes.example";

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

    [Fact]
    public async Task Production_does_not_serve_the_openapi_document()
    {
        using var application = fixture.CreateApplication(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("AllowedHosts", DeployedHost);
        });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{DeployedHost}"),
        });

        var response = await client.GetAsync("/api/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Guards `pnpm gen:api`, which reads the document from a development server.
    [Fact]
    public async Task Development_serves_the_openapi_document()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
