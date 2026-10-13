using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Archive;

/// <summary>A year whose declarations are all marked filed, with its keep-until date and when it was finished.</summary>
internal sealed record FinishedYear(int Year, KeepUntil KeepUntil, DateTimeOffset FinishedAt);

/// <summary>
/// The years whose archive the owner has yet to take away (Rule 19): every group 3 declaration of the year
/// is marked filed (Rule 15) and the owner has neither downloaded the year's ZIP nor dismissed its notice
/// since the last of those marks. Derived from the marks on every read, never scheduled, so the dashboard
/// notice and the reminder read one answer and a download clears both.
/// </summary>
internal static class ArchiveReminder
{
    /// <summary>
    /// How long after a year is finished its reminder may still go out (Rule 17). Without it the first run
    /// after this feature's deploy, or after restoring a database whose sent log is gone, would message every
    /// finished year at once; the dashboard notice still shows those years.
    /// </summary>
    public static readonly TimeSpan MessageWindow = TimeSpan.FromDays(14);

    /// <summary>The newest pending year, or null: the dashboard shows one notice and loads one year's accruals.</summary>
    public static async Task<FinishedYear?> NewestPendingAsync(
        AppDbContext database, string userId, CancellationToken cancellationToken) =>
        (await PendingAsync(database, userId, null, 1, cancellationToken)) is [var newest, ..] ? newest : null;

    /// <summary>
    /// The pending years, newest first, at most <paramref name="limit"/>. Only years whose newest mark is at
    /// or after <paramref name="markedSince"/> are looked at: a year is finished no later than its newest mark,
    /// so a caller that wants years finished since then skips loading the accruals of every older one.
    /// </summary>
    public static async Task<IReadOnlyList<FinishedYear>> PendingAsync(
        AppDbContext database,
        string userId,
        DateTimeOffset? markedSince,
        int limit,
        CancellationToken cancellationToken)
    {
        var answered = await database.ArchiveNotices.AsNoTracking()
            .Where(notice => notice.UserId == userId)
            .ToDictionaryAsync(notice => notice.Year, notice => notice.At, cancellationToken);
        // A year answered after its newest mark is answered after the mark that finished it, so it is
        // skipped before its accruals are loaded.
        var candidates = (await database.DeclarationFilings.AsNoTracking()
                .Where(filing => filing.UserId == userId)
                .GroupBy(filing => filing.Year)
                .Select(group => new { Year = group.Key, Newest = group.Max(filing => filing.CreatedAt) })
                .ToListAsync(cancellationToken))
            .Where(year => markedSince is not { } since || year.Newest >= since)
            .Where(year => !answered.TryGetValue(year.Year, out var at) || at < year.Newest)
            .Select(year => year.Year)
            .OrderDescending();

        var pending = new List<FinishedYear>();
        foreach (var year in candidates)
        {
            if (await KeepUntilEndpoints.LoadAsync(database, userId, year, cancellationToken)
                    is { KeepUntil: { } keepUntil, FinishedAt: { } finishedAt }
                && !(answered.TryGetValue(year, out var at) && at >= finishedAt))
            {
                pending.Add(new FinishedYear(year, keepUntil, finishedAt));
                if (pending.Count >= limit)
                {
                    break;
                }
            }
        }

        return pending;
    }
}

/// <summary>
/// The reminder to download a finished year's archive, once per channel under the key of its year, on the
/// first run that finds it pending within <see cref="ArchiveReminder.MessageWindow"/> of the mark that
/// finished it. A second mark, an unmark and a new mark, or a restart finds the year's claim and sends nothing.
/// It needs no particular hour: the owner has just marked the declaration.
/// </summary>
internal sealed class ArchiveIncidentSource(TimeProvider time) : IIncidentSource
{
    public async Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        return
        [
            .. (await ArchiveReminder.PendingAsync(
                    database, userId, now - ArchiveReminder.MessageWindow, int.MaxValue, cancellationToken))
                .Where(year => now - year.FinishedAt <= ArchiveReminder.MessageWindow)
                .Select(year => new Incident(
                    $"{IncidentKind.Archive}:{year.Year}", IncidentKind.Archive, null, year.Year, KeepUntil: year.KeepUntil)),
        ];
    }
}
