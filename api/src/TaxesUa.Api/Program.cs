using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

var builder = WebApplication.CreateBuilder(args);

var allowedHosts = (builder.Configuration["AllowedHosts"] ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// The proxy lists below trust every hop, so X-Forwarded-Host lets any caller choose the host the
// Google redirect_uri is built from. Pinning it to the deployed domain is the only defense, and an
// operator who forgets the variable must not silently get the fail-open wildcard.
if (builder.Environment.IsProduction() && (allowedHosts.Length == 0 || allowedHosts.Contains("*")))
{
    throw new InvalidOperationException(
        "ALLOWED_HOSTS must name the deployed domains, semicolon-separated, in Production. "
        + "A missing or wildcard value lets any caller choose the host the Google redirect_uri is "
        + "built from. Use docker-compose.local.yml for a local run.");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts = allowedHosts;
});

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<EmailAllowlist>();

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authentication.AddIdentityCookies();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

// The Google handler is a request handler: the authentication middleware initialises it on every
// request, and its options validation rejects an empty ClientId. Registering it unconfigured would
// turn every request into a 500, so local development skips it and /auth/login/google reports 503.
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;

        // Traefik publishes only `web`, and web/next.config.ts rewrites just /api/:path* to this
        // container, so a callback outside /api/ would never reach the Google handler at all.
        options.CallbackPath = "/api/auth/callback/google";
        options.SignInScheme = IdentityConstants.ExternalScheme;

        // The handler maps sub, name, email and picture and nothing else, so the callback's
        // email_verified check would find no claim at all without this mapping.
        options.ClaimActions.MapJsonKey(AuthEndpoints.EmailVerifiedClaim, "email_verified");
    });
}

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "taxesua.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

// The web build reads this document through `pnpm gen:api` against a development server and
// commits the generated types, so a deployment has no reason to describe itself to a caller.
if (app.Environment.IsDevelopment())
{
    api.MapOpenApi("/openapi/{documentName}.json");
}

api.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var payload = new { status = dbOk ? "ok" : "degraded", database = dbOk };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

api.MapAuthApi();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();
