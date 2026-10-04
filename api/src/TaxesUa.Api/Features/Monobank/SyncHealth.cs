using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Banking;

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
/// progress (a caught-up cursor, a backfill's latest batch or its start), which moves on with every recovery, so a second incident never reuses the first's key.
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

        if (accounts.Count == 0)
        {
            return null;
        }

        // A backfilling account's progress is its latest import batch: its cursor trails by design.
        var backfilling = accounts.Where(account => account.HistoryImportedAt is null).Select(account => account.Id).ToList();
        var batches = backfilling.Count == 0
            ? []
            : (await database.ImportBatches.AsNoTracking()
                .Where(row => backfilling.Contains(row.BankAccountId))
                .GroupBy(row => row.BankAccountId)
                .Select(group => new { Id = group.Key, At = group.Max(row => row.CreatedAt) })
                .ToListAsync(cancellationToken))
                .ToDictionary(row => row.Id, row => row.At);

        return Evaluate(connection, accounts, batches, now);
    }

    /// <param name="lastBatchAt">
    /// When each backfilling account last imported a window. An account with no entry has made no progress
    /// yet and is judged from <see cref="BankAccount.BackfillStartedAt"/>; a batch older than that start belongs to a
    /// backfill since reset.
    /// </param>
    public static SyncHealth Evaluate(
        MonobankConnection connection,
        IReadOnlyCollection<BankAccount> accounts,
        IReadOnlyDictionary<Guid, DateTimeOffset> lastBatchAt,
        DateTimeOffset now)
    {
        // The shown "last sync" counts only accounts that have caught up; a backfilling cursor trails by design.
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

        // Staleness is the oldest progress: a caught-up account's cursor, a backfilling account's latest batch.
        var progress = accounts
            .Select(account => account.HistoryImportedAt is not null
                ? account.SyncedThrough
                : lastBatchAt.TryGetValue(account.Id, out var batchAt) && batchAt > account.BackfillStartedAt
                    ? batchAt
                    : account.BackfillStartedAt)
            .Min();

        return progress is { } oldest && now - oldest > StaleAfter
            ? new SyncHealth(SyncHealthState.Stale, oldest, oldest)
            : new SyncHealth(SyncHealthState.Healthy, last, last ?? connection.ConnectedAt);
    }
}
