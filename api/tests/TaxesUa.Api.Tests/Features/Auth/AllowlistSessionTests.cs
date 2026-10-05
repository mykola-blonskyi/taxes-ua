using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class AllowlistSessionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string SessionCookie = "__Host-taxesua.auth";

    [Fact]
    public async Task An_address_removed_from_the_allowlist_loses_its_live_session()
    {
        var email = $"leaver-{Guid.NewGuid():N}@example.com";
        var keys = Directory.CreateTempSubdirectory("taxes-keys-").FullName;
        try
        {
            using var before = CreateApplication(keys, $"{ApiFixture.AllowedEmail};{email}");
            var cookie = await SignInCookie(before, email);

            // The allowlist is read at start, so removing an address is a restart: a second application
            // over the same key ring, which still decrypts the cookie.
            using var after = CreateApplication(keys, ApiFixture.AllowedEmail);
            var response = await Me(after, cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var cleared = Assert.Single(
                response.Headers.GetValues("Set-Cookie"),
                header => header.StartsWith($"{SessionCookie}=", StringComparison.Ordinal));
            Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(keys, recursive: true);
        }
    }

    [Fact]
    public async Task An_allowed_address_keeps_its_session_across_a_restart()
    {
        var keys = Directory.CreateTempSubdirectory("taxes-keys-").FullName;
        try
        {
            using var before = CreateApplication(keys, ApiFixture.AllowedEmail);
            var cookie = await SignInCookie(before, ApiFixture.AllowedEmail);

            using var after = CreateApplication(keys, ApiFixture.AllowedEmail);

            Assert.Equal(HttpStatusCode.OK, (await Me(after, cookie)).StatusCode);
        }
        finally
        {
            Directory.Delete(keys, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateApplication(string keysPath, string allowedEmails) =>
        fixture.CreateApplication(builder =>
        {
            builder.UseSetting("DataProtection:KeysPath", keysPath);
            builder.UseSetting("Auth:AllowedEmails", allowedEmails);
        });

    // The raw ticket, so a second application can be sent the cookie the first one issued.
    private static async Task<string> SignInCookie(WebApplicationFactory<Program> application, string email)
    {
        using var client = ApiFixture.CreateClient(application);
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var header = Assert.Single(
            callback.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{SessionCookie}=", StringComparison.Ordinal));
        return header[(SessionCookie.Length + 1)..header.IndexOf(';')];
    }

    private static async Task<HttpResponseMessage> Me(WebApplicationFactory<Program> application, string cookie)
    {
        using var client = ApiFixture.CreateClient(application);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"{SessionCookie}={cookie}");
        return await client.SendAsync(request);
    }
}
