using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class AuthEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string RejectedEmail = "stranger@example.com";

    [Fact]
    public async Task Me_without_a_session_is_unauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Callback_rejects_an_email_outside_the_allowlist()
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, RejectedEmail);

        var response = await client.GetAsync("/api/auth/callback");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.False(string.IsNullOrWhiteSpace(problem?.Title), "the 403 carries no explanation");
        Assert.DoesNotContain(RejectedEmail, body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/callback")).StatusCode);

        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users;
        Assert.False(
            await users.AnyAsync(user => user.Email == RejectedEmail),
            "a rejected email was provisioned as a user");
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("False")]
    public async Task Callback_rejects_an_email_google_has_not_verified(string emailVerified)
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, ApiFixture.SecondAllowedEmail, emailVerified);

        var response = await client.GetAsync("/api/auth/callback");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.False(string.IsNullOrWhiteSpace(problem?.Title), "the 403 carries no explanation");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users;
        Assert.False(
            await users.AnyAsync(user => user.Email == ApiFixture.SecondAllowedEmail),
            "an unverified email was provisioned as a user");
    }

    [Fact]
    public async Task Callback_signs_in_an_allowlisted_email_and_returns_a_hardened_cookie()
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, ApiFixture.AllowedEmail);

        var response = await client.GetAsync("/api/auth/callback");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        var sessionCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("taxesua.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(ApiFixture.AllowedEmail, me?.Email);
        Assert.Equal("Test Owner", me?.DisplayName);
        Assert.NotEqual(default, me?.CreatedAt);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/callback")).StatusCode);
    }

    // Traefik terminates TLS and forwards plain http to the api, which is the hop the default
    // SameAsRequest policy drops the Secure flag on.
    [Fact]
    public async Task The_external_sign_in_cookie_is_hardened_behind_a_plain_http_hop()
    {
        using var client = fixture.CreateClient("http://localhost");

        var response = await SignInExternally(client, ApiFixture.AllowedEmail);

        var externalCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith($"{IdentityConstants.ExternalScheme}=", StringComparison.Ordinal));
        Assert.Contains("httponly", externalCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", externalCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", externalCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("", "/")]
    [InlineData("/\t/evil.example", "/")]
    [InlineData("/\n/evil.example", "/")]
    [InlineData("/dashboard?tab=1", "/dashboard?tab=1")]
    public async Task Callback_redirects_only_to_a_local_path(string returnUrl, string expected)
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, ApiFixture.AllowedEmail);

        var response = await client.GetAsync($"/api/auth/callback?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, ApiFixture.AllowedEmail);
        var callback = await client.GetAsync("/api/auth/callback");
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_discards_a_pending_external_sign_in()
    {
        using var client = fixture.CreateClient();
        await SignInExternally(client, ApiFixture.AllowedEmail);

        var logout = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var callback = await client.GetAsync("/api/auth/callback");
        Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
    }

    // "True" is what the Google handler's claim mapping writes for a JSON boolean, pinned by
    // GoogleClaimMappingTests.
    private static async Task<HttpResponseMessage> SignInExternally(
        HttpClient client,
        string email,
        string emailVerified = "True")
    {
        var response = await client.PostAsync(
            $"/test-external-signin?email={Uri.EscapeDataString(email)}&emailVerified={Uri.EscapeDataString(emailVerified)}",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response;
    }
}
