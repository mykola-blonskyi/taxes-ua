using Microsoft.EntityFrameworkCore;

namespace TaxesUa.Api.Data;

/// <summary>
/// The owner's lock: a Postgres advisory lock on the owner's id, held to the end of the current database
/// transaction. Restore, the bank sync and the owner's own writes to rows those can
/// change all take it, so none of them lands between another's read and write. The mutual exclusion holds
/// only because every caller takes this one key.
/// </summary>
internal static class OwnerLock
{
    public static Task AcquireAsync(AppDbContext database, string userId, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({userId}))", cancellationToken);
}
