using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaxesUa.Api.Data;

// Writes a compressed dump of the database the connection string names to output, or throws.
internal delegate Task DatabaseDump(string connectionString, Stream output, CancellationToken ct);

// AgeRecipient is the public half of the owner's recovery key, the one the nightly backup encrypts to
// (ADR-031). RequireEncryption refuses to migrate without it, so Production never writes a plain dump.
internal sealed record MigrationDumpOptions(
    string? Directory,
    int Keep = MigrationDump.DefaultKeep,
    string? AgeRecipient = null,
    bool RequireEncryption = false);

// ADR-027. A migration only moves forward, so the database is dumped to a volume before any pending
// one runs. If the dump cannot be written the migration does not run: the database stays on the old
// schema and the exception stops the process, with the reason in the log.
internal static class MigrationDump
{
    public const string FilePrefix = "taxes_ua-pre-migrate-";

    public const int DefaultKeep = 3;

    private const string PlainExtension = ".dump";

    private const string EncryptedExtension = ".dump.age";

    public static async Task MigrateAsync(
        AppDbContext db,
        MigrationDumpOptions options,
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

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            logger.LogWarning(
                "{Count} migration(s) are pending and Migrations:DumpDirectory is not set, so no dump is taken.",
                pending.Count);
        }
        else
        {
            await TakeDumpAsync(db, options.Directory, options, dump, clock, logger, pending, ct);
        }

        await db.Database.MigrateAsync(ct);
    }

    private static async Task TakeDumpAsync(
        AppDbContext db,
        string directory,
        MigrationDumpOptions options,
        DatabaseDump dump,
        TimeProvider clock,
        ILogger logger,
        List<string> pending,
        CancellationToken ct)
    {
        var recipient = string.IsNullOrWhiteSpace(options.AgeRecipient) ? null : options.AgeRecipient.Trim();
        if (recipient is null && options.RequireEncryption)
        {
            logger.LogCritical(
                "{Count} migration(s) are pending and Migrations:DumpAgeRecipient is not set, so the dump cannot be "
                + "encrypted. The migrations were NOT run. Set BACKUP_AGE_RECIPIENT and redeploy.",
                pending.Count);
            throw new InvalidOperationException(
                "The pre-migration dump has no age recipient; migrations were not run.");
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "empty";
        var stem = $"-from-{applied}-to-{pending[^1]}.dump";
        if (Directory.Exists(directory) && DumpFiles(directory).Any(file => file.Name.Contains(stem, StringComparison.Ordinal)))
        {
            // A restart loop retries the same migration; the dump taken before the first try still holds the data.
            logger.LogInformation("A dump for this migration already exists in {Directory}, not taking another.", directory);
            return;
        }

        if (recipient is null)
        {
            logger.LogWarning("Migrations:DumpAgeRecipient is not set, so the pre-migration dump is written unencrypted.");
        }

        var name = $"{FilePrefix}{clock.GetUtcNow():yyyyMMddTHHmmssZ}{stem}{(recipient is null ? "" : ".age")}";
        var path = Path.Combine(directory, name);
        var partial = path + ".partial";

        logger.LogInformation(
            "{Count} pending migration(s), dumping the database to {Path} before they run.", pending.Count, path);
        try
        {
            Directory.CreateDirectory(directory);
            var connectionString = db.Database.GetConnectionString()!;
            Task Write(Stream output) => dump(connectionString, output, ct);
            var written = recipient is null
                ? await WritePlainAsync(partial, Write)
                : await WriteEncryptedAsync(recipient, partial, Write, ct);
            if (written == 0)
            {
                throw new InvalidOperationException("the dump wrote nothing");
            }

            File.Move(partial, path);
        }
        catch (Exception exception)
        {
            TryDelete(partial);
            logger.LogCritical(
                exception,
                "The pre-migration dump failed, so the {Count} pending migration(s) were NOT run and the "
                + "database is still on the old schema. Fix the dump (volume, permissions, pg_dump, age) and redeploy.",
                pending.Count);
            throw new InvalidOperationException("The pre-migration dump failed; migrations were not run.", exception);
        }

        Prune(directory, options.Keep, logger);
    }

    private static async Task<long> WritePlainAsync(string path, Func<Stream, Task> write)
    {
        await using var file = File.Create(path);
        var counting = new CountingStream(file);
        await write(counting);
        return counting.Written;
    }

    // The dump goes into age's stdin and age writes the file, so no plain byte of it reaches the disk.
    private static async Task<long> WriteEncryptedAsync(
        string recipient, string path, Func<Stream, Task> write, CancellationToken ct)
    {
        var start = new ProcessStartInfo("age") { RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--encrypt", "--recipient", recipient, "--output", path })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("age did not start");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var counting = new CountingStream(process.StandardInput.BaseStream);
        // age exiting early (a malformed recipient, a full disk) breaks the pipe on a write or on the close;
        // its own message, read below, says why.
        IOException? pipeError = null;
        try
        {
            await write(counting);
        }
        catch (IOException exception)
        {
            pipeError = exception;
        }
        catch
        {
            // The dump failed. age must be gone before the caller deletes the partial file it may still be writing.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException exception)
            {
                pipeError ??= exception;
            }
        }

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"age exited with {process.ExitCode}: {(await stderr).Trim()}", pipeError);
        }

        if (pipeError is not null)
        {
            throw pipeError;
        }

        return counting.Written;
    }

    private static IEnumerable<FileInfo> DumpFiles(string directory) =>
        new DirectoryInfo(directory)
            .GetFiles(FilePrefix + "*")
            .Where(file => file.Name.EndsWith(PlainExtension, StringComparison.Ordinal)
                           || file.Name.EndsWith(EncryptedExtension, StringComparison.Ordinal));

    // A pruning failure never blocks the migration: the dump that matters is already on disk.
    private static void Prune(string directory, int keep, ILogger logger)
    {
        try
        {
            foreach (var old in DumpFiles(directory)
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
    public static async Task PgDumpAsync(string connectionString, Stream output, CancellationToken ct)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var start = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "--format=custom", "--compress=6", "--no-owner" })
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
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
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

    // An encrypted file is never empty, even when the dump wrote nothing, so the bytes are counted on the way in.
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Written += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
