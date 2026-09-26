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

    // An empty or misspelled ASPNETCORE_ENVIRONMENT is a third state that is neither Development nor
    // Production, so IsProduction() skipped the check above and appsettings.json's "*" applied.
    [Theory]
    [InlineData("Developement")]
    [InlineData(" Development ")]
    [InlineData("Staging")]
    public void An_unrecognised_environment_is_pinned_like_production(string environment)
    {
        using var application = fixture.CreateApplication(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AllowedHosts", "*");
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("ALLOWED_HOSTS", failure.ToString(), StringComparison.Ordinal);
    }

    // Only docker-compose.local.yml selects Development, and it pins no domain. Reaching this state
    // means the local override is running on a real host, which also publishes the sign-in seam.
    [Fact]
    public void Development_refuses_to_start_with_a_deployed_host_pinned()
    {
        using var application = fixture.CreateApplication(builder =>
        {
            builder.UseSetting("AllowedHosts", DeployedHost);
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("Development", failure.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public async Task Production_does_not_serve_the_development_sign_in()
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

        var response = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Development_serves_the_development_sign_in()
    {
        using var client = fixture.CreateClient();

        var login = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
