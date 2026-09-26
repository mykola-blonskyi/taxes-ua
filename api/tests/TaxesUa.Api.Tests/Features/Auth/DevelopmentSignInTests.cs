using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class DevelopmentSignInTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string RejectedEmail = "stranger@example.com";

    [Fact]
    public async Task It_creates_a_real_session_for_an_allowlisted_email()
    {
        using var client = fixture.CreateClient();

        var login = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);

        var sessionCookie = Assert.Single(
            callback.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("taxesua.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(ApiFixture.AllowedEmail, me?.Email);
    }

    [Fact]
    public async Task It_refuses_an_email_outside_the_allowlist()
    {
        using var client = fixture.CreateClient();

        var login = await client.GetAsync($"/api/auth/login/development?email={RejectedEmail}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, callback.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users;
        Assert.False(
            await users.AnyAsync(user => user.Email == RejectedEmail),
            "a rejected email was provisioned as a user");
    }

    [Theory]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("", "/")]
    [InlineData("/\t/evil.example", "/")]
    [InlineData("/\n/evil.example", "/")]
    [InlineData("/dashboard?tab=1", "/dashboard?tab=1")]
    public async Task It_redirects_only_to_a_local_path(string returnUrl, string expected)
    {
        using var client = fixture.CreateClient();

        var login = await client.GetAsync(
            $"/api/auth/login/development?email={ApiFixture.AllowedEmail}&returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var callback = await client.GetAsync(login.Headers.Location);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal(expected, callback.Headers.Location?.OriginalString);
    }
}
