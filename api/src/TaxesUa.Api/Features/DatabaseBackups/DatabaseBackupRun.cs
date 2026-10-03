namespace TaxesUa.Api.Features.DatabaseBackups;

internal enum DatabaseBackupJob
{
    Backup,
    RestoreCheck,
}

/// <summary>
/// One run of the backup sidecar, written by it with psql after the run: the nightly dump or the weekly
/// restore check. The table name, the column names and the two <see cref="DatabaseBackupJob"/> strings are
/// that script's contract. It is operational state of the whole database, not owner data, so it has no
/// owner, is not audited and is not in the owner's JSON export.
/// </summary>
internal sealed class DatabaseBackupRun
{
    public long Id { get; set; }

    public DatabaseBackupJob Job { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public bool Succeeded { get; set; }

    public string Detail { get; set; } = string.Empty;
}
