using System.Net;
using System.Text.Json;

namespace TaxesUa.Api.Tests;

// ADR-024. The test host answers as https://localhost, so that is the app's own origin here.
public sealed class CrossSiteGuardTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string RotateFeed = "/api/calendar/feed/rotate";

    private static HttpRequestMessage Post(string path, string? site = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (site is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", site);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    [Theory]
    [InlineData("cross-site", null)]
    [InlineData("same-site", null)]
    [InlineData("none", null)]
    [InlineData(null, "https://todo.blonskyi.dev")]
    [InlineData(null, "null")]
    [InlineData(null, "http://localhost")]
    // Sec-Fetch-Site decides alone when the browser sends it.
    [InlineData("same-site", "https://localhost")]
    public async Task A_cross_site_unsafe_request_is_refused_with_a_stable_code(string? site, string? origin)
    {
        using var owner = await ApiFixture.SignIn(fixture.CreateApplication(_ => { }), ApiFixture.AllowedEmail);

        using var response = await owner.SendAsync(Post(RotateFeed, site, origin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("cross_site_request", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Every_unsafe_method_is_checked(string method)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/health");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("same-origin", null)]
    [InlineData(null, "https://localhost")]
    [InlineData(null, "HTTPS://LOCALHOST")]
    [InlineData("same-origin", "https://localhost")]
    public async Task A_same_origin_request_is_accepted(string? site, string? origin)
    {
        using var owner = await ApiFixture.SignIn(fixture.CreateApplication(_ => { }), ApiFixture.AllowedEmail);

        using var response = await owner.SendAsync(Post(RotateFeed, site, origin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A browser sends one of the two headers on every unsafe request, so a request with neither is a
    // script or a tool, which a web page cannot steer into riding the owner's cookie.
    [Fact]
    public async Task A_request_with_neither_header_is_not_a_browser_request_and_is_accepted()
    {
        using var owner = await ApiFixture.SignIn(fixture.CreateApplication(_ => { }), ApiFixture.AllowedEmail);

        using var response = await owner.SendAsync(Post(RotateFeed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Safe_methods_are_not_checked()
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // monobank's server posts to the secret path with no browser; the guard must not stand in front of
    // the secret's own check, which answers 404 for a secret nobody holds.
    [Theory]
    [InlineData(null)]
    [InlineData("cross-site")]
    public async Task The_monobank_webhook_is_exempt(string? site)
    {
        using var client = fixture.CreateClient();

        using var response = await client.SendAsync(Post("/api/monobank/webhook/no-such-secret", site));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_calendar_feed_and_the_health_check_are_reachable_cross_site()
    {
        using var client = fixture.CreateClient();

        foreach (var path in new[] { "/api/calendar/feed/no-such-secret.ics", "/api/health" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            using var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // Behind the proxy the scheme and host are the public ones UseForwardedHeaders restored, and the
    // origin has no port when the port is the default.
    [Theory]
    [InlineData("https", "taxes.example", "https://taxes.example", false)]
    [InlineData("https", "taxes.example", "https://evil.taxes.example", true)]
    [InlineData("https", "taxes.example", "http://taxes.example", true)]
    [InlineData("http", "localhost:3000", "http://localhost:3000", false)]
    [InlineData("http", "localhost:3000", "http://localhost:3001", true)]
    public void The_origin_is_compared_with_the_public_scheme_and_host(string scheme, string host, string origin, bool refused)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/tax-years/2026/verify";
        context.Request.Scheme = scheme;
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString(host);
        context.Request.Headers.Origin = origin;

        Assert.Equal(refused, CrossSiteGuard.IsRefused(context.Request));
    }
}
