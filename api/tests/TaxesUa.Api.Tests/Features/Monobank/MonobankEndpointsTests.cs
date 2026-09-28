using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

// The seam is the same one SettingsEndpointsTests and FxEndpointsTests use: the real app over a real
// PostgreSQL container, with monobank replaced at the HTTP message handler level (ApiFixture.CreateApplication(StubMonobankHandler)).
public sealed class MonobankEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string GoodToken = "good-token";

    [Fact]
    public async Task A_valid_token_saves_and_lists_accounts_with_fop_ones_preselected()
    {
        var body = StubMonobankHandler.ClientInfo(
            "client-1",
            ("acc-fop-1", "fop", 980, "UA000000000000000000000000001"),
            ("acc-black-1", "black", 980, "UA000000000000000000000000002"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        Assert.NotNull(status);
        Assert.True(status.Connected);

        var fop = Assert.Single(status.Accounts, a => a.ExternalId == "acc-fop-1");
        Assert.True(fop.IsFop);
        Assert.True(fop.IsSupported);
        Assert.True(fop.IsFollowed);
        Assert.Equal("UAH", fop.Currency);
        Assert.EndsWith("0001", fop.MaskedIban);
        Assert.DoesNotContain("UA000000000000000000000000001", fop.MaskedIban, StringComparison.Ordinal);

        var black = Assert.Single(status.Accounts, a => a.ExternalId == "acc-black-1");
        Assert.False(black.IsFop);
        Assert.False(black.IsSupported);
        Assert.False(black.IsFollowed);
    }

    [Fact]
    public async Task An_invalid_token_is_rejected_with_a_field_error_and_stores_nothing()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("acc-1", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "wrong-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("token", problem, StringComparison.Ordinal);

        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await UserId(database, ApiFixture.SecondAllowedEmail);
        Assert.False(await database.MonobankConnections.AnyAsync(c => c.UserId == userId));
        Assert.False(await database.BankAccounts.AnyAsync(a => a.UserId == userId));
    }

    [Fact]
    public async Task An_unknown_account_type_is_listed_as_not_supported_and_never_fails_the_request()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("acc-diia", "diia", 980, "UA9"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        var diia = status!.Accounts.Single(a => a.ExternalId == "acc-diia");
        Assert.False(diia.IsFop);
        Assert.False(diia.IsSupported);
        Assert.False(diia.IsFollowed);
    }

    [Fact]
    public async Task Choosing_accounts_persists_and_a_non_fop_id_is_ignored()
    {
        var body = StubMonobankHandler.ClientInfo(
            "client-1",
            ("fop-a", "fop", 980, "UA1"),
            ("fop-b", "fop", 840, "UA2"),
            ("black", "black", 980, "UA3"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var response = await owner.PutAsJsonAsync(
            "/api/monobank/accounts",
            new { followedExternalIds = new[] { "fop-a", "black" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        Assert.True(status!.Accounts.Single(a => a.ExternalId == "fop-a").IsFollowed);
        Assert.False(status.Accounts.Single(a => a.ExternalId == "fop-b").IsFollowed);
        Assert.False(status.Accounts.Single(a => a.ExternalId == "black").IsFollowed, "a non-FOP account must never be followable");

        var reread = await owner.GetFromJsonAsync<ConnectionStatus>("/api/monobank/connection");
        Assert.True(reread!.Accounts.Single(a => a.ExternalId == "fop-a").IsFollowed);
    }

    [Fact]
    public async Task Replacing_the_token_deactivates_accounts_the_new_client_info_no_longer_lists()
    {
        var both = StubMonobankHandler.ClientInfo(
            "client-1",
            ("deactivate-a", "fop", 980, "UA1"),
            ("deactivate-b", "fop", 980, "UA2"));
        var onlyB = StubMonobankHandler.ClientInfo("client-1", ("deactivate-b", "fop", 980, "UA2"));
        using var application = fixture.CreateApplication(ForTokens((GoodToken, both), ("second-token", onlyB)));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "second-token" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        var a = status!.Accounts.Single(acc => acc.ExternalId == "deactivate-a");
        Assert.False(a.IsFollowed, "an account dropped from the new client-info must stop being followed");
        var b = status.Accounts.Single(acc => acc.ExternalId == "deactivate-b");
        Assert.True(b.IsFollowed);
    }

    [Fact]
    public async Task Replacing_the_token_keeps_an_account_the_owner_stopped_following_unfollowed()
    {
        var both = StubMonobankHandler.ClientInfo(
            "client-1",
            ("keep-a", "fop", 980, "UA1"),
            ("keep-b", "fop", 840, "UA2"));
        using var application = fixture.CreateApplication(ForTokens((GoodToken, both), ("second-token", both)));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });
        await owner.PutAsJsonAsync("/api/monobank/accounts", new { followedExternalIds = new[] { "keep-a" } });

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "second-token" });

        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        Assert.True(status!.Accounts.Single(acc => acc.ExternalId == "keep-a").IsFollowed);
        Assert.False(status.Accounts.Single(acc => acc.ExternalId == "keep-b").IsFollowed);
    }

    [Fact]
    public async Task An_account_that_reappears_waits_for_the_owner_to_follow_it_again()
    {
        var both = StubMonobankHandler.ClientInfo(
            "client-1",
            ("reappear-a", "fop", 980, "UA1"),
            ("reappear-b", "fop", 980, "UA2"));
        var onlyB = StubMonobankHandler.ClientInfo("client-1", ("reappear-b", "fop", 980, "UA2"));
        using var application = fixture.CreateApplication(ForTokens(
            (GoodToken, both), ("second-token", onlyB), ("third-token", both)));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "second-token" });

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "third-token" });

        var status = await response.Content.ReadFromJsonAsync<ConnectionStatus>();
        Assert.False(status!.Accounts.Single(acc => acc.ExternalId == "reappear-a").IsFollowed);
        Assert.True(status.Accounts.Single(acc => acc.ExternalId == "reappear-b").IsFollowed);
    }

    [Fact]
    public async Task Replacing_a_working_token_with_an_invalid_one_keeps_the_connection_and_accounts()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("acc-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var rejected = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var status = await owner.GetFromJsonAsync<ConnectionStatus>("/api/monobank/connection");
        Assert.True(status!.Connected);
        Assert.True(status.Accounts.Single(acc => acc.ExternalId == "acc-a").IsFollowed);
    }

    [Fact]
    public async Task An_empty_accounts_body_is_a_validation_problem_not_a_server_error()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("acc-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var response = await owner.PutAsync(
            "/api/monobank/accounts",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_null_followed_accounts_list_is_a_validation_problem_not_a_server_error()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("acc-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var response = await owner.PutAsJsonAsync(
            "/api/monobank/accounts",
            new { followedExternalIds = (string[]?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"clientId":123,"name":"x","accounts":[]}""")]
    [InlineData("""{"clientId":"c","name":"x","accounts":[{"id":123,"type":"fop","currencyCode":980,"iban":"UA1"}]}""")]
    [InlineData("""{"clientId":"c","name":"x","accounts":[{"id":"a","type":123,"currencyCode":980,"iban":"UA1"}]}""")]
    [InlineData("""{"clientId":"c","name":"x","accounts":[{"id":"a","type":"fop","currencyCode":980,"iban":123}]}""")]
    [InlineData("""{"clientId":"c","name":"x","accounts":[{"id":"a","type":"fop","currencyCode":99999999999,"iban":"UA1"}]}""")]
    public async Task A_malformed_client_info_payload_never_crashes_to_a_server_error(string malformedBody)
    {
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, malformedBody));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", problem, StringComparison.Ordinal);
    }

    // Answers client-info for each recognised token with its own body, and 403 for any other one, so
    // a single test can exercise "save, then replace with a different token" against one stub.
    private static StubMonobankHandler ForTokens(params (string Token, string Body)[] byToken) =>
        new(request =>
        {
            var token = request.Headers.TryGetValues("X-Token", out var values) ? values.FirstOrDefault() : null;
            var match = byToken.FirstOrDefault(entry => entry.Token == token);
            return match.Token is not null
                ? StubMonobankHandler.Json(match.Body)
                : new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

    [Fact]
    public async Task A_second_owner_sees_no_accounts_or_connection_from_the_first()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("fop-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        var status = await other.GetFromJsonAsync<ConnectionStatus>("/api/monobank/connection");

        Assert.NotNull(status);
        Assert.False(status.Connected);
        Assert.Empty(status.Accounts);
    }

    [Fact]
    public async Task Disconnect_removes_the_token_but_keeps_bank_account_rows()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("fop-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var disconnect = await owner.DeleteAsync("/api/monobank/connection");

        Assert.Equal(HttpStatusCode.NoContent, disconnect.StatusCode);
        var status = await owner.GetFromJsonAsync<ConnectionStatus>("/api/monobank/connection");
        Assert.NotNull(status);
        Assert.False(status.Connected);
        Assert.Contains(status.Accounts, a => a.ExternalId == "fop-a");

        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await UserId(database, ApiFixture.AllowedEmail);
        Assert.False(await database.MonobankConnections.AnyAsync(c => c.UserId == userId));
        Assert.True(await database.BankAccounts.AnyAsync(a => a.UserId == userId && a.ExternalId == "fop-a"));
    }

    [Fact]
    public async Task The_token_never_appears_in_any_monobank_response()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("fop-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var put = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });
        var putBody = await put.Content.ReadAsStringAsync();
        var get = await owner.GetStringAsync("/api/monobank/connection");

        Assert.DoesNotContain(GoodToken, putBody, StringComparison.Ordinal);
        Assert.DoesNotContain(GoodToken, get, StringComparison.Ordinal);
        Assert.DoesNotContain("EncryptedToken", putBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EncryptedToken", get, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_token_never_appears_in_a_backup()
    {
        var body = StubMonobankHandler.ClientInfo("client-1", ("fop-a", "fop", 980, "UA1"));
        using var application = fixture.CreateApplication(StubMonobankHandler.ForToken(GoodToken, body));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync("/api/monobank/connection", new { token = GoodToken });

        var backup = await owner.GetStringAsync("/api/backup");

        Assert.DoesNotContain(GoodToken, backup, StringComparison.Ordinal);
        Assert.DoesNotContain("monobank", backup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bankAccount", backup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_route_answers_service_unavailable_when_no_key_is_configured()
    {
        using var application = fixture.CreateApplicationWithoutMonobankKey();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var get = await owner.GetAsync("/api/monobank/connection");
        var put = await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "anything" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, put.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/monobank/connection")]
    [InlineData("PUT", "/api/monobank/connection")]
    [InlineData("PUT", "/api/monobank/accounts")]
    [InlineData("DELETE", "/api/monobank/connection")]
    public async Task Every_route_without_a_session_is_unauthorized(string method, string path)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { token = "x", followedExternalIds = Array.Empty<string>() });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> UserId(AppDbContext database, string email) =>
        await database.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

    private sealed record ConnectionStatus(bool Connected, IReadOnlyList<ConnectionAccount> Accounts);

    private sealed record ConnectionAccount(
        string ExternalId,
        string Bank,
        string Currency,
        string MaskedIban,
        bool IsFop,
        bool IsSupported,
        bool IsFollowed);
}
