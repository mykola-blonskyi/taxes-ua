using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Monobank;

internal abstract record JarOutcome
{
    private JarOutcome() { }

    public sealed record Listed(IReadOnlyList<MonobankJar> Jars, DateTimeOffset At) : JarOutcome;

    public sealed record Stored(ReserveJar Jar) : JarOutcome;

    public sealed record NotConnected : JarOutcome;

    public sealed record NoJarChosen : JarOutcome;

    // The jar is not among the UAH jars the bank reports now: unknown, closed, or in another currency.
    public sealed record JarNotOffered : JarOutcome;

    public sealed record Waiting(TimeSpan RetryAfter) : JarOutcome;

    public sealed record InvalidToken : JarOutcome;

    public sealed record Unavailable(string Reason) : JarOutcome;
}

/// <summary>
/// Reads the owner's jars through <see cref="MonobankClientInfoReader"/> and keeps the one chosen for taxes.
/// Only a UAH jar is offered, so the stored balance needs no conversion. The token is decrypted here and
/// goes no further than the reader; it is never logged and never part of an outcome.
/// </summary>
internal sealed class ReserveJarService(
    AppDbContext database,
    TokenEncryptor encryptor,
    MonobankClientInfoReader reader,
    MonobankClient client,
    ILogger<ReserveJarService> logger)
{
    // Later than the nightly run (ADR-012) and the webhook syncs would leave a balance, so a balance this
    // old means refreshes have been failing and the owner is told its time.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    public static bool IsStale(DateTimeOffset fetchedAt, DateTimeOffset now) => now - fetchedAt > StaleAfter;

    public Task<JarOutcome> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        ReadAsync(ownerId, cancellationToken);

    public async Task<JarOutcome> ChooseAsync(string ownerId, string jarId, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(ownerId, cancellationToken);
        if (read is not JarOutcome.Listed found)
        {
            return read;
        }

        if (found.Jars.FirstOrDefault(jar => jar.Id == jarId) is not { } chosen)
        {
            return new JarOutcome.JarNotOffered();
        }

        var row = await database.ReserveJars.FindAsync([ownerId], cancellationToken);
        if (row is null)
        {
            row = new ReserveJar { UserId = ownerId };
            database.ReserveJars.Add(row);
        }

        Apply(row, chosen, found.At);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (database.Entry(row).State == EntityState.Added)
        {
            // A double submit inserted the owner's row first: this choice is then an update of it.
            database.Entry(row).State = EntityState.Detached;
            var existing = await database.ReserveJars.FindAsync([ownerId], cancellationToken);
            if (existing is null)
            {
                throw;
            }

            Apply(existing, chosen, found.At);
            await database.SaveChangesAsync(cancellationToken);
            row = existing;
        }

        return new JarOutcome.Stored(row);
    }

    /// <summary>
    /// Updates the chosen jar's balance. The write names the jar it read and only moves the fetch time
    /// forward, so a choice changed, cleared or restored meanwhile is not overwritten by this answer.
    /// </summary>
    public async Task<JarOutcome> RefreshAsync(string ownerId, CancellationToken cancellationToken)
    {
        var chosen = await database.ReserveJars.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == ownerId, cancellationToken);
        if (chosen is null)
        {
            return new JarOutcome.NoJarChosen();
        }

        var read = await ReadAsync(ownerId, cancellationToken);
        if (read is not JarOutcome.Listed found)
        {
            return read;
        }

        if (found.Jars.FirstOrDefault(jar => jar.Id == chosen.JarId) is not { } jar)
        {
            // Kept as it was: the owner sees the balance with the time it was true, and can pick another jar.
            logger.LogWarning("The reserve jar of owner {OwnerId} is no longer among the UAH jars of its token.", ownerId);
            return new JarOutcome.JarNotOffered();
        }

        var title = Fit(jar.Title);
        await database.ReserveJars
            .Where(row => row.UserId == ownerId && row.JarId == chosen.JarId && row.FetchedAt <= found.At)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Title, title)
                    .SetProperty(row => row.BalanceKop, jar.BalanceKop)
                    .SetProperty(row => row.FetchedAt, found.At),
                cancellationToken);

        var stored = await database.ReserveJars.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == ownerId, cancellationToken);
        return stored is null ? new JarOutcome.NoJarChosen() : new JarOutcome.Stored(stored);
    }

    public Task ClearAsync(string ownerId, CancellationToken cancellationToken) =>
        database.ReserveJars.Where(row => row.UserId == ownerId).ExecuteDeleteAsync(cancellationToken);

    // Listed holds the UAH jars only.
    private async Task<JarOutcome> ReadAsync(string ownerId, CancellationToken cancellationToken)
    {
        var connection = await database.MonobankConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == ownerId, cancellationToken);
        if (connection is null || connection.RejectedAt is not null || !encryptor.IsConfigured)
        {
            return new JarOutcome.NotConnected();
        }

        string token;
        try
        {
            token = encryptor.Decrypt(connection.EncryptedToken);
        }
        catch (CryptographicException exception)
        {
            logger.LogError(exception, "The monobank token of owner {OwnerId} could not be decrypted.", ownerId);
            return new JarOutcome.Unavailable("The stored monobank token cannot be read.");
        }

        return await reader.ReadAsync(client, ownerId, token, cancellationToken) switch
        {
            ClientInfoRead.Found found => new JarOutcome.Listed(Offered(found.Info.Jars), found.At),
            ClientInfoRead.Waiting waiting => new JarOutcome.Waiting(waiting.RetryAfter),
            ClientInfoRead.InvalidToken => new JarOutcome.InvalidToken(),
            ClientInfoRead.Unavailable unavailable => new JarOutcome.Unavailable(unavailable.Reason),
            _ => throw new InvalidOperationException($"Unhandled {nameof(ClientInfoRead)}."),
        };
    }

    // Only the hryvnia jars, and only ones whose id fits the column.
    private static List<MonobankJar> Offered(IReadOnlyList<MonobankJar> jars) =>
        [.. jars.Where(jar => IsoCurrency.FromNumeric(jar.CurrencyCode) == Currency.UAH
            && jar.Id.Length <= ReserveJarConfiguration.MaxJarIdLength)];

    private static void Apply(ReserveJar row, MonobankJar jar, DateTimeOffset at)
    {
        row.JarId = jar.Id;
        row.Title = Fit(jar.Title);
        row.BalanceKop = jar.BalanceKop;
        row.FetchedAt = at;
    }

    private static string Fit(string title) =>
        title.Length <= ReserveJarConfiguration.MaxTitleLength ? title : title[..ReserveJarConfiguration.MaxTitleLength];
}
