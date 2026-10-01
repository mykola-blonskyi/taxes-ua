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

    [Theory]
    [InlineData("ConnectionStrings:Default", "DATABASE_URL")]
    [InlineData("Authentication:Google:ClientId", "GOOGLE_CLIENT_ID")]
    [InlineData("Authentication:Google:ClientSecret", "GOOGLE_CLIENT_SECRET")]
    [InlineData("Auth:AllowedEmails", "ALLOWED_EMAILS")]
    public void Production_refuses_to_start_without_a_required_variable(string key, string variable)
    {
        using var application = fixture.CreateApplication(builder =>
        {
            Deployed(builder);
            builder.UseSetting(key, "");
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains(variable, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("taxes.example")]
    [InlineData("ftp://taxes.example")]
    [InlineData("https://taxes.example/?from=reminder")]
    public void A_malformed_public_url_refuses_to_start(string publicUrl)
    {
        using var application = fixture.CreateApplication(builder =>
        {
            Deployed(builder);
            builder.UseSetting("App:PublicUrl", publicUrl);
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("APP_PUBLIC_URL", failure.ToString(), StringComparison.Ordinal);
    }

    // The allowlist splits on both separators, so a value made only of them allows nobody.
    [Theory]
    [InlineData("   ")]
    [InlineData(" , ")]
    [InlineData(";\t,")]
    public void Production_refuses_an_allowlist_that_names_nobody(string allowedEmails)
    {
        using var application = fixture.CreateApplication(builder =>
        {
            Deployed(builder);
            builder.UseSetting("Auth:AllowedEmails", allowedEmails);
        });

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("ALLOWED_EMAILS", failure.ToString(), StringComparison.Ordinal);
    }

    // Traefik reaches web by the domain; web's rewrite reaches api as `api:8080` and forwards the
    // domain in X-Forwarded-Host; the healthcheck asks `localhost:8080` directly.
    [Theory]
    [InlineData("https://localhost:8080", null, HttpStatusCode.OK)]
    [InlineData("http://api:8080", DeployedHost, HttpStatusCode.OK)]
    [InlineData("http://api:8080", "evil.example", HttpStatusCode.BadRequest)]
    [InlineData("http://api:8080", "localhost", HttpStatusCode.BadRequest)]
    [InlineData("https://evil.example", null, HttpStatusCode.BadRequest)]
    public async Task Production_accepts_only_the_domain_and_its_own_internal_names(
        string address, string? forwardedHost, HttpStatusCode expected)
    {
        using var application = fixture.CreateApplication(Deployed);
        using var client = ApiFixture.CreateClient(application, address);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        if (forwardedHost is not null)
        {
            request.Headers.Add("X-Forwarded-Host", forwardedHost);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_session_survives_a_restart_when_the_key_ring_is_persisted()
    {
        var keys = Directory.CreateTempSubdirectory("taxes-ua-keys-");
        try
        {
            string sessionCookie;
            using (var before = fixture.CreateApplication(builder =>
                builder.UseSetting("DataProtection:KeysPath", keys.FullName)))
            {
                using var client = ApiFixture.CreateClient(before);
                var login = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");
                var callback = await client.GetAsync(login.Headers.Location);
                sessionCookie = callback.Headers.GetValues("Set-Cookie")
                    .Single(header => header.StartsWith("taxesua.auth=", StringComparison.Ordinal))
                    .Split(';')[0];
            }

            Assert.NotEmpty(keys.GetFiles("key-*.xml"));

            using var after = fixture.CreateApplication(builder =>
                builder.UseSetting("DataProtection:KeysPath", keys.FullName));
            using var restarted = new HttpClient(after.Server.CreateHandler()) { BaseAddress = new Uri("https://localhost") };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Add("Cookie", sessionCookie);

            var me = await restarted.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Production_does_not_serve_the_openapi_document()
    {
        using var application = fixture.CreateApplication(Deployed);
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
        using var application = fixture.CreateApplication(Deployed);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{DeployedHost}"),
        });

        var response = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void Deployed(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("AllowedHosts", DeployedHost);
        builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
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
