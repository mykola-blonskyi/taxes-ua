using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Banking;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Turns <see cref="SyncHealth"/> into an incident for the alert sender. The key is the kind and the
/// moment the state began, so an incident is alerted once however long it lasts and a later one, which
/// begins at a later moment, is a new key. While any followed account is queued or running, a stale or
/// unreadable-token incident is not reported: accounts recover one at a time and the oldest cursor moves
/// with each, which would re-key a recovery already under way.
/// </summary>
internal sealed class SyncIncidentSource(TimeProvider time, MonobankSyncQueue queue) : IIncidentSource
{
    public async Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var health = await SyncHealthCheck.LoadAsync(database, userId, time.GetUtcNow(), cancellationToken);
        var kind = health?.State switch
        {
            SyncHealthState.Stale => IncidentKind.SyncStale,
            SyncHealthState.TokenRejected => IncidentKind.TokenRejected,
            SyncHealthState.TokenUnreadable => IncidentKind.TokenUnreadable,
            _ => (IncidentKind?)null,
        };

        if (health is null || kind is not { } open)
        {
            return [];
        }

        if (open != IncidentKind.TokenRejected)
        {
            var accountIds = await database.BankAccounts.AsNoTracking()
                .Where(row => row.UserId == userId && row.Bank == Bank.Monobank && row.IsFop && row.IsActive)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken);
            if (accountIds.Any(id => queue.IsPending(new SyncWork(userId, id))))
            {
                return [];
            }
        }

        return [new Incident($"{open}:{health.Since.ToUnixTimeSeconds()}", open, health.LastSyncedAt)];
    }
}
