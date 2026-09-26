using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;

var builder = WebApplication.CreateBuilder(args);

var allowedHosts = (builder.Configuration["AllowedHosts"] ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// The proxy lists below trust every hop, so X-Forwarded-Host lets any caller choose the host the
// Google redirect_uri is built from. Pinning the domain is the only defense, so a forgotten
// variable has to stop the deployment instead of falling open. The test is `!IsDevelopment()` and
// not `IsProduction()`: an empty or misspelled ASPNETCORE_ENVIRONMENT is neither, and that third
// state would skip this check and fall back to appsettings.json's wildcard.
if (!builder.Environment.IsDevelopment() && (allowedHosts.Length == 0 || allowedHosts.Contains("*")))
{
    throw new InvalidOperationException(
        "ALLOWED_HOSTS must name the deployed domains, semicolon-separated, outside Development. "
        + "A missing or wildcard value lets any caller choose the host the Google redirect_uri is "
        + "built from. Use docker-compose.local.yml for a local run.");
}

// docker-compose.local.yml is the only thing that selects Development, and it pins no domain. A
// process in this state is that local override running on a real host, where it would also publish
// the Development-only sign-in seam registered further down.
if (builder.Environment.IsDevelopment() && allowedHosts.Length > 0 && !allowedHosts.Contains("*"))
{
    throw new InvalidOperationException(
        "ALLOWED_HOSTS pins a deployed domain while the environment is Development, which publishes "
        + "the Development-only sign-in seam on that domain. Deploy docker-compose.yml without the "
        + "local override, or leave ALLOWED_HOSTS unset for a local run.");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts = allowedHosts;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // web/ reads these shapes as TypeScript generated from the OpenAPI document, where a numeric enum
    // arrives as a magic number instead of a string union. allowIntegerValues also defaults to true,
    // and the number path checks no enum member, so `{"paymentMode": 77}` would otherwise be stored.
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));

    // Both default to off, which lets a request body omit a non-nullable member and reach a handler
    // with null in it. On, the serializer answers 400 and no handler needs a null guard.
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
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
    // Answering 401 rather than redirecting is also what stops web/src/proxy.ts looping: a 302 to a
    // login path would come back through the Next rewrite as another gated request.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

// The external cookie carries the pending Google sign-in, and Traefik forwards plain http to this
// container, so the default SameAsRequest policy would let that cookie ride an unencrypted hop.
builder.Services.ConfigureExternalCookie(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);

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

    // A deliberate auth bypass for obtaining a real session without a Google OAuth client. This
    // Development check, plus the allowlist inside AuthEndpoints.CompleteSignIn, are what contain it.
    api.MapDevelopmentSignIn();
}

api.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var payload = new { status = dbOk ? "ok" : "degraded", database = dbOk };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

api.MapAuthApi();
api.MapSettingsApi();
api.MapTaxYearsApi();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();
