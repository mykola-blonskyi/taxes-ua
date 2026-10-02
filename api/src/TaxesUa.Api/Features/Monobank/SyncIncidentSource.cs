using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Turns <see cref="SyncHealth"/> into an incident for the alert sender. The key is the kind and the
/// moment the state began, so an incident is alerted once however long it lasts and a later one, which
/// begins at a later moment, is a new key.
/// </summary>
internal sealed class SyncIncidentSource(TimeProvider time) : IIncidentSource
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

        return health is null || kind is not { } open
            ? []
            : [new Incident($"{open}:{health.Since.ToUnixTimeSeconds()}", open, health.LastSyncedAt)];
    }
}
