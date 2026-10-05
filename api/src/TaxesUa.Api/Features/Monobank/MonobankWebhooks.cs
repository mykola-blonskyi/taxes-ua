using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Banking;

namespace TaxesUa.Api.Features.Monobank;

internal abstract record WebhookWork(string OwnerId)
{
    // Make the bank's webhook for the owner's current token what the app wants it to be.
    public sealed record Reconcile(string OwnerId) : WebhookWork(OwnerId);

    // Remove the webhook of a token the owner disconnected, which the app no longer stores.
    public sealed record Clear(string OwnerId, byte[] EncryptedToken) : WebhookWork(OwnerId);
}

internal enum WebhookState
{
    // No public base URL is configured, so no webhook is ever registered.
    Off,
    Pending,
    Registered,
    Failed,
}

/// <summary>
/// Registers each owner's webhook at <c>{Monobank:PublicBaseUrl}/api/monobank/webhook/{secret}</c>
/// (ADR-012), one call at a time on the rate gate's "webhook" slot, so a token save never waits on
/// the bank for it. Only the secret's hash is stored (#256), so every registration draws a new secret
/// and the URL it replaces stops answering. Without a public base URL nothing is registered and a URL
/// registered by an earlier deployment is removed.
/// </summary>
internal sealed class MonobankWebhooks : BackgroundService
{
    // Where MonobankEndpoints maps the webhook within the /api group.
    public const string Route = CrossSiteGuard.MonobankWebhookRoute;

    public const string PathPrefix = "/api" + Route;

    private const string WebhookMethod = "webhook";

    private readonly Channel<WebhookWork> _channel =
        Channel.CreateUnbounded<WebhookWork>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, byte> _waiting = new();

    private int _outstanding;

    private readonly string? _baseUrl;

    private readonly IServiceScopeFactory _scopes;

    private readonly TokenEncryptor _encryptor;

    private readonly MonobankRateGate _gate;

    private readonly TimeProvider _time;

    private readonly ILogger<MonobankWebhooks> _logger;

    public MonobankWebhooks(
        IConfiguration configuration,
        IServiceScopeFactory scopes,
        TokenEncryptor encryptor,
        MonobankRateGate gate,
        TimeProvider time,
        ILogger<MonobankWebhooks> logger)
    {
        _baseUrl = ParseBaseUrl(configuration["Monobank:PublicBaseUrl"]);
        _scopes = scopes;
        _encryptor = encryptor;
        _gate = gate;
        _time = time;
        _logger = logger;
    }

    public bool IsConfigured => _baseUrl is not null;

    // A rejected token is never registered until it is replaced, so it cannot stay Pending.
    public WebhookState StateOf(MonobankConnection connection) => _baseUrl switch
    {
        null => WebhookState.Off,
        _ when connection.RejectedAt is not null => WebhookState.Failed,
        var wanted when wanted == connection.WebhookBaseUrl => WebhookState.Registered,
        _ when connection.WebhookFailedAt is not null => WebhookState.Failed,
        _ => WebhookState.Pending,
    };

    /// <summary>True when no registration or removal is waiting or running. Tests use it to know that
    /// nothing was asked of the bank, rather than sleeping and hoping it had time to.</summary>
    public bool IsIdle => Volatile.Read(ref _outstanding) == 0;

    public void Reconcile(string ownerId)
    {
        if (_waiting.TryAdd(ownerId, 0))
        {
            Enqueue(new WebhookWork.Reconcile(ownerId));
        }
    }

    public void Clear(string ownerId, byte[] encryptedToken) => Enqueue(new WebhookWork.Clear(ownerId, encryptedToken));

    private void Enqueue(WebhookWork work)
    {
        Interlocked.Increment(ref _outstanding);
        _channel.Writer.TryWrite(work);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileStaleAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "monobank webhooks could not be checked at startup.");
        }

        await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (work)
                {
                    case WebhookWork.Reconcile reconcile:
                        _waiting.TryRemove(reconcile.OwnerId, out _);
                        await ReconcileAsync(reconcile.OwnerId, stoppingToken);
                        break;
                    case WebhookWork.Clear clear:
                        await ClearAsync(clear, stoppingToken);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The monobank webhook of owner {OwnerId} was not updated.", work.OwnerId);
            }
            finally
            {
                Interlocked.Decrement(ref _outstanding);
            }
        }
    }

    // The registered URL can differ from the wanted one after a restart that lost a pending
    // registration, or a deploy that changed or removed the public base URL.
    private async Task ReconcileStaleAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await database.MonobankConnections.AsNoTracking()
            .Where(row => row.RejectedAt == null && row.WebhookBaseUrl != _baseUrl)
            .Select(row => row.UserId)
            .ToListAsync(cancellationToken);
        foreach (var ownerId in stale)
        {
            Reconcile(ownerId);
        }
    }

    private async Task ReconcileAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = await database.MonobankConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == ownerId && row.RejectedAt == null, cancellationToken);
        if (connection is null)
        {
            return;
        }

        if (_baseUrl is null && connection.WebhookBaseUrl is null)
        {
            return;
        }

        // Only the token this registration was made for: one saved meanwhile queues its own.
        var sameToken = database.MonobankConnections
            .Where(row => row.UserId == ownerId && row.EncryptedToken == connection.EncryptedToken);
        if (Decrypt(ownerId, connection.EncryptedToken) is not { } token)
        {
            await sameToken.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WebhookFailedAt, _time.GetUtcNow())
                    .SetProperty(row => row.WebhookFailure, SyncFailure.TokenUnreadable),
                cancellationToken);
            return;
        }

        await _gate.WaitTurnAsync(ownerId, WebhookMethod, cancellationToken);

        // The bank checks the URL with a GET before it accepts it, so the new secret has to answer first.
        // The stored hash is the only copy, so the URL registered before stops answering here.
        var secret = _baseUrl is null ? null : PathSecret.New();
        var hash = secret is null ? null : PathSecret.Hash(secret);
        var drawn = await sameToken.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.WebhookSecretHash, hash)
                .SetProperty(row => row.WebhookBaseUrl, (string?)null),
            cancellationToken);
        if (drawn == 0)
        {
            return;
        }

        var client = scope.ServiceProvider.GetRequiredService<MonobankClient>();
        var url = secret is null ? string.Empty : _baseUrl + PathPrefix + secret;
        switch (await client.SetWebhookAsync(token, url, cancellationToken))
        {
            case WebhookResult.Set:
                await sameToken.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.WebhookBaseUrl, _baseUrl)
                        .SetProperty(row => row.WebhookFailedAt, (DateTimeOffset?)null)
                        .SetProperty(row => row.WebhookFailure, (SyncFailure?)null),
                    cancellationToken);
                break;

            case WebhookResult.InvalidToken:
                _logger.LogWarning("monobank rejected the token of owner {OwnerId} when setting its webhook.", ownerId);
                // The first rejection stays: RejectedAt keys the incident, so a later one must not move it.
                await sameToken.Where(row => row.RejectedAt == null).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.RejectedAt, _time.GetUtcNow()), cancellationToken);
                break;

            case WebhookResult.Unavailable unavailable:
                _logger.LogWarning(
                    "monobank webhook of owner {OwnerId} was not set: {Failure}.", ownerId, unavailable.Failure);
                await sameToken.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.WebhookFailedAt, _time.GetUtcNow())
                        .SetProperty(row => row.WebhookFailure, unavailable.Failure),
                    cancellationToken);
                break;
        }
    }

    private async Task ClearAsync(WebhookWork.Clear clear, CancellationToken cancellationToken)
    {
        if (Decrypt(clear.OwnerId, clear.EncryptedToken) is not { } token)
        {
            return;
        }

        await _gate.WaitTurnAsync(clear.OwnerId, WebhookMethod, cancellationToken);
        await using var scope = _scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<MonobankClient>()
            .SetWebhookAsync(token, string.Empty, cancellationToken);
        if (result is not WebhookResult.Set)
        {
            _logger.LogWarning(
                "The webhook of owner {OwnerId}'s disconnected token was not removed: {Result}.", clear.OwnerId, result);
        }
    }

    private string? Decrypt(string ownerId, byte[] encryptedToken)
    {
        try
        {
            return _encryptor.Decrypt(encryptedToken, ownerId);
        }
        catch (CryptographicException exception)
        {
            _logger.LogError(exception, "The monobank token of owner {OwnerId} could not be decrypted.", ownerId);
            return null;
        }
    }

    private static string? ParseBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Monobank:PublicBaseUrl (MONOBANK_PUBLIC_BASE_URL) must be an absolute http or https URL.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
