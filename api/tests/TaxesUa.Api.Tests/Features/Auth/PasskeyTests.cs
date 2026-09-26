using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class PasskeyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string RejectedEmail = "passkey-stranger@example.com";

    private const string BogusCredential =
        """{"id":"AAAA","rawId":"AAAA","type":"public-key","response":{},"clientExtensionResults":{}}""";

    [Fact]
    public async Task An_invalid_attestation_is_rejected()
    {
        using var client = fixture.CreateClient();
        await SignIn(client, ApiFixture.AllowedEmail);
        await Ceremony(client, "/api/auth/passkey/register/options");

        var response = await Submit(client, "/api/auth/passkey/register", BogusCredential);

        await AssertStatus(HttpStatusCode.BadRequest, response);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.False(string.IsNullOrWhiteSpace(problem?.Title), "the 400 carries no explanation");
    }

    [Fact]
    public async Task An_invalid_assertion_is_rejected()
    {
        using var client = fixture.CreateClient();
        await Ceremony(client, "/api/auth/passkey/login/options");

        var response = await Submit(client, "/api/auth/passkey/login", BogusCredential);

        await AssertStatus(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task An_assertion_with_no_ceremony_underway_is_rejected()
    {
        using var client = fixture.CreateClient();

        var response = await Submit(client, "/api/auth/passkey/login", BogusCredential);

        await AssertStatus(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task A_registration_with_no_ceremony_underway_is_rejected()
    {
        using var client = fixture.CreateClient();
        await SignIn(client, ApiFixture.AllowedEmail);

        var response = await Submit(client, "/api/auth/passkey/register", BogusCredential);

        await AssertStatus(HttpStatusCode.BadRequest, response);
    }

    [Theory]
    [InlineData("""{"credentialJson":""}""")]
    [InlineData("""{"credentialJson":"   "}""")]
    [InlineData("{}")]
    public async Task An_empty_credential_is_rejected(string body)
    {
        using var client = fixture.CreateClient();
        await Ceremony(client, "/api/auth/passkey/login/options");

        var response = await client.PostAsync(
            "/api/auth/passkey/login",
            new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertStatus(HttpStatusCode.BadRequest, response);
    }

    [Theory]
    [InlineData("/api/auth/passkey/register/options")]
    [InlineData("/api/auth/passkey/register")]
    public async Task Registering_a_passkey_requires_a_session(string path)
    {
        using var client = fixture.CreateClient();

        var response = await Submit(client, path, BogusCredential);

        await AssertStatus(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public void The_api_refuses_to_start_without_a_passkey_server_domain()
    {
        using var application = fixture.CreateApplication(builder =>
            builder.UseSetting("Auth:Passkey:ServerDomain", string.Empty));

        var failure = Record.Exception(() => application.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("PASSKEY_SERVER_DOMAIN", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assertion_for_an_email_outside_the_allowlist_is_refused()
    {
        await Seed(RejectedEmail);
        using var application = AssertingAs(RejectedEmail);
        using var client = Client(application);
        await Ceremony(client, "/api/auth/passkey/login/options");

        var response = await Submit(client, "/api/auth/passkey/login", BogusCredential);

        await AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_assertion_for_an_allowlisted_email_signs_in()
    {
        await Seed(ApiFixture.SecondAllowedEmail);
        using var application = AssertingAs(ApiFixture.SecondAllowedEmail);
        using var client = Client(application);
        await Ceremony(client, "/api/auth/passkey/login/options");

        var response = await Submit(client, "/api/auth/passkey/login", BogusCredential);

        await AssertStatus(HttpStatusCode.NoContent, response);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(ApiFixture.SecondAllowedEmail, me?.Email);
    }

    private WebApplicationFactory<Program> AssertingAs(string email) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPasskeyHandler<ApplicationUser>>(provider => new StubbedCrypto(
                provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<IOptions<IdentityPasskeyOptions>>(),
                email))));

    private static HttpClient Client(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private async Task Seed(string email)
    {
        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var created = await users.CreateAsync(new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(error => error.Description)));
    }

    private static async Task SignIn(HttpClient client, string email)
    {
        var login = await client.GetAsync($"/api/auth/login/development?email={Uri.EscapeDataString(email)}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
    }

    private static async Task Ceremony(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, content: null);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, string path, string credentialJson) =>
        client.PostAsJsonAsync(path, new { credentialJson });

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == expected,
            $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
    }

    // Only the crypto is stubbed. Both options ceremonies run for real, and so does the allowlist
    // gate under test; a genuine assertion would need a real authenticator.
    private sealed class StubbedCrypto(
        UserManager<ApplicationUser> users,
        IOptions<IdentityPasskeyOptions> options,
        string email) : IPasskeyHandler<ApplicationUser>
    {
        private readonly PasskeyHandler<ApplicationUser> _real = new(users, options);

        public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(
            PasskeyUserEntity userEntity,
            HttpContext httpContext) => _real.MakeCreationOptionsAsync(userEntity, httpContext);

        public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(
            ApplicationUser? user,
            HttpContext httpContext) => _real.MakeRequestOptionsAsync(user, httpContext);

        public Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context) =>
            _real.PerformAttestationAsync(context);

        public async Task<PasskeyAssertionResult<ApplicationUser>> PerformAssertionAsync(
            PasskeyAssertionContext context)
        {
            var user = await users.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"{email} was not seeded");

            return PasskeyAssertionResult.Success(
                new UserPasskeyInfo(
                    credentialId: Encoding.UTF8.GetBytes($"credential-for-{email}"),
                    publicKey: [1, 2, 3],
                    createdAt: DateTimeOffset.UtcNow,
                    signCount: 1,
                    transports: ["internal"],
                    isUserVerified: true,
                    isBackupEligible: false,
                    isBackedUp: false,
                    attestationObject: [],
                    clientDataJson: []),
                user);
        }
    }
}
