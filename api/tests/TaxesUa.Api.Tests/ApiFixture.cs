using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Auth;
using Testcontainers.PostgreSql;

namespace TaxesUa.Api.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    public const string AllowedEmail = "owner@example.com";

    public const string SecondAllowedEmail = "second@example.com";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplicationFactory<Program> _application = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        _application = CreateApplication(_ => { });
    }

    public WebApplicationFactory<Program> CreateApplication(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _database.GetConnectionString(),
                    ["Auth:AllowedEmails"] = $" {AllowedEmail} ; {SecondAllowedEmail}",
                }));

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, ExternalSignInStub>());

            configure(builder);
        });

    public async Task DisposeAsync()
    {
        await _application.DisposeAsync();
        await _database.DisposeAsync();
    }

    public HttpClient CreateClient() => _application.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

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
