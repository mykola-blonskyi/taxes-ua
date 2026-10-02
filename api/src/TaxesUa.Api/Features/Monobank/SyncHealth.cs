using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Monobank;

internal enum SyncHealthState
{
    Healthy,
    Stale,
    TokenRejected,
    TokenUnreadable,
}

/// <summary>
/// How trustworthy the bank-synced figures are right now (Rule 17). <c>LastSyncedAt</c> is the oldest
/// cursor among the followed accounts that have caught up, null while none has. <c>Since</c> is the
/// moment the current state began, which keys an incident: a rejection's own time, otherwise the last
/// good sync, which moves on with every recovery, so a second incident never reuses the first's key.
/// </summary>
internal sealed record SyncHealth(SyncHealthState State, DateTimeOffset? LastSyncedAt, DateTimeOffset Since);

internal static class SyncHealthCheck
{
    // Longer than a missed night plus a bank outage over a weekend, short enough that a stopped sync is
    // noticed well before the next deadline. Not a tax parameter, so it lives here and not in TaxYearConfig.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    /// <summary>Null when the owner follows no account, so there is no sync to be healthy or not.</summary>
    public static async Task<SyncHealth?> LoadAsync(
        AppDbContext database, string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = await database.MonobankConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var accounts = await database.BankAccounts.AsNoTracking()
            .Where(row => row.UserId == userId && row.Bank == Bank.Monobank && row.IsFop && row.IsActive)
            .ToListAsync(cancellationToken);

        return accounts.Count == 0 ? null : Evaluate(connection, accounts, now);
    }

    public static SyncHealth Evaluate(MonobankConnection connection, IReadOnlyCollection<BankAccount> accounts, DateTimeOffset now)
    {
        // An account still backfilling history has a cursor that trails by design, so it is not judged by age.
        DateTimeOffset? last = accounts
            .Where(account => account.HistoryImportedAt is not null && account.SyncedThrough is not null)
            .Select(account => account.SyncedThrough)
            .Min();

        if (connection.RejectedAt is { } rejectedAt)
        {
            return new SyncHealth(SyncHealthState.TokenRejected, last, rejectedAt);
        }

        // Like a rejection, this outranks staleness: one cause, one alert.
        if (accounts.Any(account => account.LastFailure == SyncFailure.TokenUnreadable))
        {
            return new SyncHealth(SyncHealthState.TokenUnreadable, last, last ?? connection.ConnectedAt);
        }

        return last is { } synced && now - synced > StaleAfter
            ? new SyncHealth(SyncHealthState.Stale, last, synced)
            : new SyncHealth(SyncHealthState.Healthy, last, last ?? connection.ConnectedAt);
    }
}
