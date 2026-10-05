using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Api.Tests.Features.Monobank;
using TaxesUa.Api.Tests.Features.Notifications;
using Testcontainers.PostgreSql;

namespace TaxesUa.Api.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    public const string AllowedEmail = "owner@example.com";

    public const string SecondAllowedEmail = "second@example.com";

    // Allowlisted but not an admin: the one address tests use to prove a tax-year write is refused.
    public const string NonAdminEmail = "member@example.com";

    // 32 bytes, base64 — a fixed test key so every test runs with monobank "configured" unless it
    // deliberately asks for the unconfigured application below.
    public const string MonobankTestKeyBase64 = "dGVzdC1tb25vYmFuay1rZXktMzItYnl0ZXMtbG9uZyE=";

    // The shape of a real bot token: the bot's id, a colon, a secret. Tests that must prove it never leaks
    // look for TelegramTestSecret.
    public const string TelegramTestSecret = "AAH-t3st_s3cret-of-the-bot";

    public const string TelegramTestToken = "123456:" + TelegramTestSecret;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();

    private WebApplicationFactory<Program> _application = null!;

    private readonly ConcurrentQueue<string> _owners = new();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        _application = CreateApplication(_ => { });
    }

    // An owner no other test shares. Hosted workers act on the rows in the class-wide database, including
    // those an earlier test left unfinished, so a test that shares an owner can find its sync, its
    // connection or its linked channel already moved on by another test's worker. Mint the owner before
    // the application is created: an application allowlists the owners minted so far.
    public string NewOwner()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        _owners.Enqueue(email);
        return email;
    }

    public WebApplicationFactory<Program> CreateApplication(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Program.cs reads these while it is still registering services, which is before a
            // ConfigureAppConfiguration source is attached, so they have to be host settings.
            builder.UseSetting("Auth:Passkey:ServerDomain", "localhost");
            builder.UseSetting("ConnectionStrings:Default", _database.GetConnectionString());
            builder.UseSetting("Auth:AllowedEmails", string.Join(';', [AllowedEmail, SecondAllowedEmail, NonAdminEmail, .. _owners]));

            // Every address a test signs in as is an admin, because tests write tax years as an owner of their
            // own, except NonAdminEmail.
            builder.UseSetting("Auth:AdminEmails", string.Join(';', [AllowedEmail, SecondAllowedEmail, .. _owners]));
            builder.UseSetting("Monobank:TokenEncryptionKeyBase64", MonobankTestKeyBase64);

            // The Development sign-in needs this flag besides the environment; tests sign in through it.
            builder.UseSetting("Auth:DevelopmentSignIn", "true");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, ExternalSignInStub>();

                // A test that needs NBU or monobank registers its own handler after this one, which
                // replaces it.
                services.AddHttpClient<NbuRateClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubNbuHandler(_ =>
                        throw new InvalidOperationException("real NBU called from a test")));
                services.AddHttpClient<MonobankClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubMonobankHandler(_ =>
                        throw new InvalidOperationException("real monobank called from a test")));
                services.AddHttpClient<TelegramClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubTelegramHandler
                    {
                        Override = _ => throw new InvalidOperationException("real Telegram called from a test"),
                    });
            });

            configure(builder);
        });

    public async Task DisposeAsync()
    {
        await _application.DisposeAsync();
        await _database.DisposeAsync();
    }

    public HttpClient CreateClient(string origin = "https://localhost") => CreateClient(_application, origin);

    public static HttpClient CreateClient(WebApplicationFactory<Program> application, string origin = "https://localhost") =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(origin),
        });

    // An application whose NBU answers come from nbu and whose clock reads today in Kyiv.
    public WebApplicationFactory<Program> CreateApplication(StubNbuHandler nbu, DateOnly today) =>
        CreateApplication(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<NbuRateClient>().ConfigurePrimaryHttpMessageHandler(() => nbu);
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(today, new TimeOnly(10, 0), TimeSpan.Zero)));
        }));

    // An application whose monobank answers come from monobank.
    public WebApplicationFactory<Program> CreateApplication(StubMonobankHandler monobank) =>
        CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => monobank)));

    // An application whose bot token is set and whose Telegram answers come from telegram. The poller
    // stays off unless asked for, so a test drives each round itself; time is fake so a retry's backoff
    // is stepped through rather than waited for.
    public WebApplicationFactory<Program> CreateApplication(
        StubTelegramHandler telegram,
        FakeTimeProvider? time = null,
        bool runPoller = false,
        Action<IWebHostBuilder>? configure = null) =>
        CreateApplication(builder =>
        {
            telegram.Clock = time;
            builder.UseSetting("Telegram:BotToken", TelegramTestToken);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<TelegramClient>().ConfigurePrimaryHttpMessageHandler(() => telegram);
                if (time is not null)
                {
                    services.AddSingleton<TimeProvider>(time);
                }

                if (!runPoller)
                {
                    services.RemoveAll<IHostedService>();
                }
            });
            configure?.Invoke(builder);
        });

    public const string SmtpTestPassword = "sm7p-t3st-p4ssw0rd";

    // An application whose SMTP settings are set (with a password the tests prove never leaks) and whose
    // outgoing email goes to email, in memory. Workers stay off unless asked for, and time is fake so a
    // retry's backoff is stepped through rather than waited for.
    internal WebApplicationFactory<Program> CreateApplication(
        InMemoryEmailTransport email,
        FakeTimeProvider? time = null,
        bool runWorkers = false,
        Action<IWebHostBuilder>? configure = null) =>
        CreateApplication(builder =>
        {
            email.Clock = time;
            builder.UseSetting("Smtp:Host", "smtp.test");
            builder.UseSetting("Smtp:Port", "587");
            builder.UseSetting("Smtp:Tls", "starttls");
            builder.UseSetting("Smtp:User", "mailer");
            builder.UseSetting("Smtp:Password", SmtpTestPassword);
            builder.UseSetting("Smtp:From", "Taxes <noreply@taxes.test>");
            builder.UseSetting("App:PublicUrl", "https://taxes.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(email);
                if (time is not null)
                {
                    services.AddSingleton<TimeProvider>(time);
                }

                if (!runWorkers)
                {
                    services.RemoveAll<IHostedService>();
                }
            });
            configure?.Invoke(builder);
        });

    // An application with no monobank key configured at all (ADR-011): every monobank endpoint must
    // answer "not configured" instead of ever reaching the encryptor or the bank.
    public WebApplicationFactory<Program> CreateApplicationWithoutMonobankKey() =>
        CreateApplication(builder => builder.UseSetting("Monobank:TokenEncryptionKeyBase64", string.Empty));

    // Tests that move a fake clock by days read the owner's session off the same clock, and the 7-day session
    // lifetime (ADR-009) would end it mid-test. The lifetime is covered by its own test, so these widen it.
    public static void KeepSessionAcrossClockJumps(IServiceCollection services) =>
        services.PostConfigure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme, options => options.ExpireTimeSpan = TimeSpan.FromDays(3650));

    public static async Task<HttpClient> SignIn(WebApplicationFactory<Program> application, string email)
    {
        var client = CreateClient(application);
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return client;
    }

    public string ConnectionString => _database.GetConnectionString();

    public AsyncServiceScope CreateScope() => _application.Services.CreateAsyncScope();

    private sealed class ExternalSignInStub : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuePipeline) =>
            {
                if (!HttpMethods.IsPost(context.Request.Method) ||
                    context.Request.Path != "/test-external-signin")
                {
                    await continuePipeline();
                    return;
                }

                var email = context.Request.Query["email"].ToString();
                var emailVerified = context.Request.Query["emailVerified"].ToString();
                var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, $"google-key-for-{email}"),
                        new Claim(ClaimTypes.Email, email),
                        new Claim(ClaimTypes.Name, "Test Owner"),
                    ],
                    GoogleDefaults.AuthenticationScheme);

                if (emailVerified.Length > 0)
                {
                    identity.AddClaim(new Claim(AuthEndpoints.EmailVerifiedClaim, emailVerified));
                }

                var properties = new AuthenticationProperties();

                // GetExternalLoginInfoAsync reads the provider name back out of the external
                // cookie's properties, and returns null when this item is missing.
                properties.Items["LoginProvider"] = GoogleDefaults.AuthenticationScheme;

                await context.SignInAsync(
                    IdentityConstants.ExternalScheme,
                    new ClaimsPrincipal(identity),
                    properties);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });

            next(app);
        };
    }
}
