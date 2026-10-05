using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Features.Calendar;
using TaxesUa.Api.Features.Clients;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.DatabaseBackups;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;

var builder = WebApplication.CreateBuilder(args);

// The request line prints the path, and two anonymous routes carry a secret in theirs: the monobank
// webhook and the calendar feed (ADR-012, ADR-017). Development raises the framework's level to
// Information, so the line is held back here for every environment.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

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

// Without any one of these a deployment cannot reach its database or cannot sign anybody in. The
// sign-in gaps would otherwise pass the health check and surface only at the first login.
// Development runs without the Google client.
string[] missing = builder.Environment.IsDevelopment()
    ? []
    : [.. new (string Key, string Variable)[]
        {
            ("ConnectionStrings:Default", "DATABASE_URL"),
            ("Authentication:Google:ClientId", "GOOGLE_CLIENT_ID"),
            ("Authentication:Google:ClientSecret", "GOOGLE_CLIENT_SECRET"),
            ("Auth:AllowedEmails", "ALLOWED_EMAILS"),
        }
        .Where(required => builder.Configuration[required.Key]?.Split(
            [',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is not { Length: > 0 })
        .Select(required => required.Variable)];
if (missing.Length > 0)
{
    throw new InvalidOperationException(
        $"Missing required configuration outside Development: {string.Join(", ", missing)}. "
        + "Set them as environment variables; .env.example lists every one.");
}

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("taxes-ua");

// The session cookie is a ticket encrypted with this key ring (ADR-009). Kept inside the container
// it dies with every redeploy and signs the owner out; docker-compose.yml mounts a volume here.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts = allowedHosts;
});

// Host filtering runs as a startup filter, ahead of UseForwardedHeaders, so it sees the Host this
// container was addressed by: `api:8080` from web's rewrite and `localhost:8080` from the
// healthcheck, never the public domain. Pinned to the domain alone it answers 400 to both. The
// domain pin that matters is ForwardedHeadersOptions.AllowedHosts above, and the check after
// UseForwardedHeaders below refuses a forwarded host it did not accept.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.Configure<HostFilteringOptions>(options =>
        options.AllowedHosts = [.. allowedHosts, "api", "localhost"]);
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // web/ reads these shapes as TypeScript generated from the OpenAPI document, where a numeric enum
    // arrives as a magic number instead of a string union, and StrictEnumJsonConverter rejects a
    // number outright. It also closes a gap JsonStringEnumConverter leaves open: Enum.TryParse accepts
    // a comma-separated list of member names for any enum and ORs them, so a body naming two members
    // (e.g. "Income, RefundToClient") would otherwise bind to whichever single member that combination
    // happens to equal, silently storing the wrong value instead of failing.
    options.SerializerOptions.Converters.Add(new StrictEnumJsonConverterFactory());

    // Both default to off, which lets a request body omit a non-nullable member and reach a handler
    // with null in it. On, the serializer answers 400 and no handler needs a null guard.
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

builder.Services.AddOpenApi(options => options.AddSchemaTransformer<EnumSchemaTransformer>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<AppDbContext>((services, options) => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));

builder.Services.AddSingleton<EmailAllowlist>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<FxRates>();
builder.Services.AddHttpClient<NbuRateClient>(client =>
{
    // Empty means the real NBU: the local Compose file passes it through, empty unless a stub is wanted.
    var baseUrl = builder.Configuration["Nbu:BaseUrl"];
    client.BaseAddress = new Uri((string.IsNullOrWhiteSpace(baseUrl) ? "https://bank.gov.ua" : baseUrl).TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// The key can be absent (a local run, or a deployment that has not set it up yet): TokenEncryptor
// starts unconfigured rather than throwing, and MonobankEndpoints answers 503 until it is set.
builder.Services.AddSingleton<TokenEncryptor>();
builder.Services.AddHttpClient<MonobankClient>(client =>
    {
        // Empty means the real bank: the local Compose file passes it through, empty unless a stub is wanted.
        var baseUrl = builder.Configuration["Monobank:BaseUrl"];
        client.BaseAddress = new Uri((string.IsNullOrWhiteSpace(baseUrl) ? "https://api.monobank.ua" : baseUrl).TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    // The framework's request logging prints the URL, and the statement path carries the bank account
    // id and the sync window.
    .RemoveAllLoggers();
builder.Services.AddSingleton<MonobankRateGate>();
builder.Services.AddSingleton<MonobankClientInfoReader>();
builder.Services.AddScoped<ReserveJarService>();
builder.Services.AddSingleton<MonobankSyncQueue>();
builder.Services.AddScoped<MonobankStatementImport>();
builder.Services.AddHostedService<MonobankSyncWorker>();
builder.Services.AddSingleton<MonobankWebhooks>();
builder.Services.AddHostedService(services => services.GetRequiredService<MonobankWebhooks>());
builder.Services.AddHostedService<MonobankNightlySync>();

// The token is optional: without it the Telegram channel reports itself unavailable and nothing polls.
// RemoveAllLoggers because the framework's request logging prints the URL, and the Bot API puts the
// token in the path.
builder.Services.AddSingleton<TelegramBot>();
builder.Services.AddHttpClient<TelegramClient>(client =>
    {
        var baseUrl = builder.Configuration["Telegram:BaseUrl"];
        client.BaseAddress = new Uri((string.IsNullOrWhiteSpace(baseUrl) ? "https://api.telegram.org" : baseUrl).TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(60);
    })
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddScoped<TelegramLinking>();
builder.Services.AddScoped<ChannelDelivery>();
builder.Services.AddScoped<TelegramDelivery>();
builder.Services.AddSingleton<TelegramPoller>();
builder.Services.AddHostedService<TelegramPollWorker>();
builder.Services.AddSingleton<AppLink>();
builder.Services.AddScoped<IReminderChannel, TelegramReminderChannel>();

// SMTP is optional like the bot token: without valid settings the email channel reports itself
// unavailable and nothing is sent. The password lives only in EmailSettings and is never logged.
builder.Services.AddSingleton<EmailSettings>();
builder.Services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
builder.Services.AddSingleton<EmailConfirmation>();
builder.Services.AddScoped<EmailDelivery>();
builder.Services.AddScoped<IReminderChannel, EmailReminderChannel>();
builder.Services.AddScoped<IIncidentSource, SyncIncidentSource>();
builder.Services.AddScoped<IIncidentSource, NewTaxYearIncidentSource>();
builder.Services.AddScoped<IIncidentSource, RestoreCheckIncidentSource>();
builder.Services.AddScoped<IIncidentSource, ExpiredTreasuryAccountIncidentSource>();
builder.Services.AddSingleton<ReminderSender>();
builder.Services.AddHostedService<ReminderWorker>();

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
    // The __Host- prefix makes browsers refuse the cookie unless it is Secure, Path=/ and has no
    // Domain, so a sibling subdomain cannot plant a same-named cookie. Do not set Domain or Path.
    options.Cookie.Name = "__Host-taxesua.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
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
builder.Services.AddPasskeys(builder.Configuration);

builder.Services.AddAuthorization();

var app = builder.Build();

// Resolved eagerly so a malformed (as opposed to merely absent) key or public URL fails startup
// instead of the first monobank request or reminder.
app.Services.GetRequiredService<TokenEncryptor>();
app.Services.GetRequiredService<MonobankWebhooks>();
app.Services.GetRequiredService<TelegramBot>();
app.Services.GetRequiredService<AppLink>();
app.Services.GetRequiredService<EmailSettings>();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        // UseForwardedHeaders removes X-Forwarded-Host once it applies it, and leaves it in place
        // when the host is not in ALLOWED_HOSTS. A request still carrying it names a host this
        // deployment does not serve, and would otherwise continue as `api` and build redirects
        // from that.
        if (context.Request.Headers.ContainsKey("X-Forwarded-Host"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // web/next.config.ts sets these on its own pages, but Next passes a rewritten /api/*
        // response through with only the headers this process wrote.
        var headers = context.Response.Headers;
        headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        await next();
    });
}
app.UseCrossSiteGuard();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

// The web build reads this document through `pnpm gen:api` against a development server and
// commits the generated types, so a deployment has no reason to describe itself to a caller.
if (app.Environment.IsDevelopment())
{
    api.MapOpenApi("/openapi/{documentName}.json");

    // A deliberate auth bypass for obtaining a real session without a Google OAuth client. It needs the
    // Development environment AND Auth:DevelopmentSignIn=true, which only docker-compose.local.yml and
    // a developer's own run set; the allowlist inside AuthEndpoints.CompleteSignIn is the third gate.
    // A loopback check on the remote address is not usable: the proxy lists above trust every hop, so
    // X-Forwarded-For chooses RemoteIpAddress, and behind web's rewrite it is a container address anyway.
    if (bool.TryParse(app.Configuration["Auth:DevelopmentSignIn"], out var developmentSignIn) && developmentSignIn)
    {
        api.MapDevelopmentSignIn();
    }
}

// The commit this container was built from. Coolify injects SOURCE_COMMIT into the compose services;
// the compose file must not mention it, or Coolify makes it an empty user variable. CI waits for it to
// appear in /api/health, which is how a new release is told apart from the old one still answering.
var release = new[] { app.Configuration["App:Release"], app.Configuration["SOURCE_COMMIT"] }
    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "unknown";

api.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var payload = new { status = dbOk ? "ok" : "degraded", database = dbOk, release };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

api.MapAuthApi();
api.MapSettingsApi();
api.MapInvoicingApi();
api.MapInvoicingPrefillApi();
api.MapInvoicesApi();
api.MapDeclarationDetailsApi();
api.MapDpsStatusApi();
api.MapTaxYearsApi();
api.MapPeriodsApi();
api.MapDeclarationsApi();
api.MapTransactionsApi();
api.MapClientsApi();
api.MapExportApi();
api.MapFxApi();
api.MapPaymentsApi();
api.MapPaymentCandidatesApi();
api.MapTreasuryAccountsApi();
api.MapPaymentDetailsApi();
api.MapBackupApi();
api.MapAuditApi();
api.MapDashboardApi();
api.MapMonobankApi();
api.MapReserveJarApi();
api.MapNotificationsApi();
api.MapCalendarApi();

await using (var scope = app.Services.CreateAsyncScope())
{
    await MigrationDump.MigrateAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        new MigrationDumpOptions(
            app.Configuration["Migrations:DumpDirectory"],
            app.Configuration.GetValue("Migrations:DumpKeep", MigrationDump.DefaultKeep),
            app.Configuration["Migrations:DumpAgeRecipient"],
            RequireEncryption: app.Environment.IsProduction()),
        MigrationDump.PgDumpAsync,
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        app.Logger);
}

app.Run();
