using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Data;

public sealed class MigrationDumpTests(ApiFixture fixture) : IClassFixture<ApiFixture>, IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"taxes-ua-dumps-{Guid.NewGuid():N}");

    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"taxes-ua-keys-{Guid.NewGuid():N}");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private readonly AsyncServiceScope _scope = fixture.CreateScope();

    public void Dispose()
    {
        _scope.Dispose();
        foreach (var directory in new[] { _directory, _keys })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_pending_migration_is_dumped_first_and_then_runs()
    {
        await using var db = await NewEmptyDatabase();
        var order = new List<string>();

        await MigrationDump.MigrateAsync(db, new MigrationDumpOptions(_directory, 5), async (_, output, _) =>
        {
            order.Add("dump:" + (await db.Database.GetAppliedMigrationsAsync()).Count());
            await Write(output, "dump");
        }, _clock, NullLogger.Instance);

        Assert.Equal(["dump:0"], order);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches(@"taxes_ua-pre-migrate-20261002T090000Z-from-empty-to-\d+_\w+\.dump$", file);
    }

    [Fact]
    public async Task With_a_recipient_the_dump_is_encrypted_and_opens_with_its_identity()
    {
        var (identity, recipient) = await NewAgeKey();
        await using var db = await NewEmptyDatabase();

        await MigrationDump.MigrateAsync(db, new MigrationDumpOptions(_directory, AgeRecipient: recipient, RequireEncryption: true),
            (_, output, _) => Write(output, "the whole database"), _clock, NullLogger.Instance);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches(@"taxes_ua-pre-migrate-20261002T090000Z-from-empty-to-\d+_\w+\.dump\.age$", file);
        Assert.DoesNotContain("the whole database", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        Assert.Equal("the whole database", await Run("age", "--decrypt", "--identity", identity, file));
    }

    [Fact]
    public async Task Production_without_a_recipient_refuses_to_migrate()
    {
        await using var db = await NewEmptyDatabase();
        var calls = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, new MigrationDumpOptions(_directory, RequireEncryption: true), (_, output, _) =>
            {
                calls++;
                return Write(output, "dump");
            }, _clock, NullLogger.Instance));

        Assert.Contains("migrations were not run", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any());
    }

    [Fact]
    public async Task A_recipient_age_rejects_stops_the_migration_and_leaves_no_file()
    {
        await using var db = await NewEmptyDatabase();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, new MigrationDumpOptions(_directory, AgeRecipient: "age1notakey", RequireEncryption: true),
            (_, output, _) => Write(output, "dump"), _clock, NullLogger.Instance));

        Assert.Contains("migrations were not run", failure.Message, StringComparison.Ordinal);
        Assert.Contains("age exited", failure.InnerException!.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_failed_dump_stops_the_migration_and_leaves_the_schema_alone()
    {
        await using var db = await NewEmptyDatabase();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, new MigrationDumpOptions(_directory, 5), (_, _, _) => throw new IOException("disk full"), _clock,
            NullLogger.Instance));

        Assert.Contains("migrations were not run", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_failed_encrypted_dump_leaves_no_partial_file()
    {
        var (_, recipient) = await NewAgeKey();
        await using var db = await NewEmptyDatabase();

        await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, new MigrationDumpOptions(_directory, AgeRecipient: recipient), async (_, output, _) =>
            {
                await Write(output, "half of it");
                throw new InvalidOperationException("pg_dump exited with 1");
            }, _clock, NullLogger.Instance));

        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_dump_that_wrote_nothing_counts_as_failed()
    {
        var (_, recipient) = await NewAgeKey();
        foreach (var options in new[]
                 {
                     new MigrationDumpOptions(_directory, 5),
                     new MigrationDumpOptions(_directory, 5, recipient),
                 })
        {
            await using var db = await NewEmptyDatabase();

            await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
                db, options, (_, _, _) => Task.CompletedTask, _clock, NullLogger.Instance));

            Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(Directory.GetFiles(_directory));
        }
    }

    [Fact]
    public async Task A_database_with_nothing_pending_is_not_dumped()
    {
        await using var db = await NewEmptyDatabase();
        await db.Database.MigrateAsync();
        var calls = 0;

        await MigrationDump.MigrateAsync(db, new MigrationDumpOptions(_directory, 5), (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }, _clock, NullLogger.Instance);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_retry_of_the_same_migration_takes_no_second_dump()
    {
        var (_, recipient) = await NewAgeKey();
        var options = new MigrationDumpOptions(_directory, 5, recipient);
        await using var first = await NewEmptyDatabase();
        var calls = 0;
        DatabaseDump dump = (_, output, _) =>
        {
            calls++;
            return Write(output, "dump");
        };
        await MigrationDump.MigrateAsync(first, options, dump, _clock, NullLogger.Instance);

        // The same pending set again, as after a crash between the dump and the migration.
        await using var retry = await NewEmptyDatabase();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await MigrationDump.MigrateAsync(retry, options, dump, _clock, NullLogger.Instance);

        Assert.Equal(1, calls);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Only_the_newest_three_dumps_are_kept_plain_or_encrypted()
    {
        var (_, recipient) = await NewAgeKey();
        Directory.CreateDirectory(_directory);
        foreach (var name in new[] { "20260101T000000Z-x.dump", "20260102T000000Z-x.dump.age", "20260103T000000Z-x.dump", "20260104T000000Z-x.dump.age" })
        {
            await File.WriteAllTextAsync(Path.Combine(_directory, MigrationDump.FilePrefix + name), "old");
        }

        await File.WriteAllTextAsync(Path.Combine(_directory, "unrelated.dump"), "mine");
        await using var db = await NewEmptyDatabase();

        await MigrationDump.MigrateAsync(db, new MigrationDumpOptions(_directory, AgeRecipient: recipient),
            (_, output, _) => Write(output, "new"), _clock, NullLogger.Instance);

        var kept = Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "taxes_ua-pre-migrate-20260103T000000Z-x.dump",
                "taxes_ua-pre-migrate-20260104T000000Z-x.dump.age",
                Assert.Single(kept, name => name!.Contains("20261002", StringComparison.Ordinal)),
                "unrelated.dump",
            ],
            kept);
    }

    [Fact]
    public async Task Without_a_dump_directory_the_migration_still_runs()
    {
        await using var db = await NewEmptyDatabase();

        await MigrationDump.MigrateAsync(db, new MigrationDumpOptions(null, 5),
            (_, _, _) => throw new InvalidOperationException("no dump expected"), _clock, NullLogger.Instance);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Health_names_the_release_the_container_was_built_from()
    {
        using var application = fixture.CreateApplication(builder => builder.UseSetting("App:Release", "abc1234"));
        using var client = ApiFixture.CreateClient(application);

        var health = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/health");

        Assert.Equal("abc1234", health!["release"].ToString());
    }

    [Fact]
    public async Task Health_falls_back_to_the_SOURCE_COMMIT_coolify_injects()
    {
        using var application = fixture.CreateApplication(builder => builder.UseSetting("SOURCE_COMMIT", "def5678"));
        using var client = ApiFixture.CreateClient(application);

        var health = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/health");

        Assert.Equal("def5678", health!["release"].ToString());
    }

    private static Task Write(Stream output, string text) => output.WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask();

    private async Task<(string IdentityFile, string Recipient)> NewAgeKey()
    {
        Directory.CreateDirectory(_keys);
        var identity = Path.Combine(_keys, $"{Guid.NewGuid():N}.txt");
        await Run("age-keygen", "-o", identity);
        return (identity, (await Run("age-keygen", "-y", identity)).Trim());
    }

    private static async Task<string> Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{program} exited with {process.ExitCode}: {await stderr}");
        return await stdout;
    }

    private async Task<AppDbContext> NewEmptyDatabase()
    {
        var name = $"dump_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name };
        // The application's own options carry what its model needs; a bare UseNpgsql sees pending model changes.
        var appOptions = _scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(appOptions)
            .UseNpgsql(connectionString.ConnectionString).Options);
    }
}
