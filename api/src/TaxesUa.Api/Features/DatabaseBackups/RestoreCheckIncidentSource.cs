using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Features.DatabaseBackups;

/// <summary>
/// Reports a restore check that fails or has gone missing, the same for every owner. The key is the
/// anchor, the last successful check or else the oldest run on record, so a failure followed by going
/// overdue is one incident and the next success moves the anchor and re-arms it (ADR-026).
/// </summary>
internal sealed class RestoreCheckIncidentSource(TimeProvider time) : IIncidentSource
{
    // A weekly check plus a day of slack.
    internal static readonly TimeSpan OverdueAfter = TimeSpan.FromDays(8);

    public async Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var checks = database.DatabaseBackupRuns.AsNoTracking().Where(run => run.Job == DatabaseBackupJob.RestoreCheck);
        var lastSuccess = await checks.Where(run => run.Succeeded)
            .Select(run => (DateTimeOffset?)run.FinishedAt)
            .MaxAsync(cancellationToken);
        var latestCheck = await checks.OrderByDescending(run => run.FinishedAt)
            .Select(run => new { run.Succeeded })
            .FirstOrDefaultAsync(cancellationToken);
        var oldestRun = await database.DatabaseBackupRuns.AsNoTracking()
            .Select(run => (DateTimeOffset?)run.FinishedAt)
            .MinAsync(cancellationToken);

        if ((lastSuccess ?? oldestRun) is not { } anchor)
        {
            return [];
        }

        var failing = latestCheck is { Succeeded: false };
        if (!failing && time.GetUtcNow() - anchor <= OverdueAfter)
        {
            return [];
        }

        return [new Incident($"{IncidentKind.RestoreCheckFailed}:{anchor.ToUnixTimeSeconds()}", IncidentKind.RestoreCheckFailed, lastSuccess)];
    }
}
