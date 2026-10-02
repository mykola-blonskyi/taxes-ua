using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaxesUa.Api.Data;

// Writes a compressed dump of the database the connection string names to outputPath, or throws.
internal delegate Task DatabaseDump(string connectionString, string outputPath, CancellationToken ct);

// ADR-027. A migration only moves forward, so the database is dumped to a volume before any pending
// one runs. If the dump cannot be written the migration does not run: the database stays on the old
// schema and the exception stops the process, with the reason in the log.
internal static class MigrationDump
{
    public const string FilePrefix = "taxes_ua-pre-migrate-";

    public const int DefaultKeep = 10;

    public static async Task MigrateAsync(
        AppDbContext db,
        string? dumpDirectory,
        int keep,
        DatabaseDump dump,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dumpDirectory))
        {
            logger.LogWarning(
                "{Count} migration(s) are pending and Migrations:DumpDirectory is not set, so no dump is taken.",
                pending.Count);
        }
        else
        {
            await TakeDumpAsync(db, dumpDirectory, keep, dump, clock, logger, pending, ct);
        }

        await db.Database.MigrateAsync(ct);
    }

    private static async Task TakeDumpAsync(
        AppDbContext db,
        string directory,
        int keep,
        DatabaseDump dump,
        TimeProvider clock,
        ILogger logger,
        List<string> pending,
        CancellationToken ct)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "empty";
        var name = $"{FilePrefix}{clock.GetUtcNow():yyyyMMddTHHmmssZ}-from-{applied}-to-{pending[^1]}.dump";
        var path = Path.Combine(directory, name);
        var partial = path + ".partial";

        logger.LogInformation(
            "{Count} pending migration(s), dumping the database to {Path} before they run.", pending.Count, path);
        try
        {
            Directory.CreateDirectory(directory);
            await dump(db.Database.GetConnectionString()!, partial, ct);
            if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
            {
                throw new InvalidOperationException("the dump file is missing or empty");
            }

            File.Move(partial, path);
        }
        catch (Exception exception)
        {
            TryDelete(partial);
            logger.LogCritical(
                exception,
                "The pre-migration dump failed, so the {Count} pending migration(s) were NOT run and the "
                + "database is still on the old schema. Fix the dump (volume, permissions, pg_dump) and redeploy.",
                pending.Count);
            throw new InvalidOperationException("The pre-migration dump failed; migrations were not run.", exception);
        }

        Prune(directory, keep, logger);
    }

    // A pruning failure never blocks the migration: the dump that matters is already on disk.
    private static void Prune(string directory, int keep, ILogger logger)
    {
        try
        {
            foreach (var old in new DirectoryInfo(directory)
                         .GetFiles(FilePrefix + "*.dump")
                         .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                         .Skip(Math.Max(keep, 1)))
            {
                old.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not prune old dumps in {Directory}.", directory);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next dump attempt overwrites the same name pattern; nothing else depends on it.
        }
    }

    // pg_dump reads its connection from PG* variables, so the password never reaches a command line.
    public static async Task PgDumpAsync(string connectionString, string outputPath, CancellationToken ct)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var start = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "--format=custom", "--compress=6", "--no-owner", "--file", outputPath })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            var other => other.ToString().ToLowerInvariant(),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pg_dump did not start");
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pg_dump exited with {process.ExitCode}: {await stderr}");
        }
    }
}
