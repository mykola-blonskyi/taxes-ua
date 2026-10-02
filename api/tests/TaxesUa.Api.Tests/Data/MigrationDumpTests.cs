using System.Net.Http.Json;
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

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private readonly AsyncServiceScope _scope = fixture.CreateScope();

    public void Dispose()
    {
        _scope.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_pending_migration_is_dumped_first_and_then_runs()
    {
        await using var db = await NewEmptyDatabase();
        var order = new List<string>();

        await MigrationDump.MigrateAsync(db, _directory, 5, async (_, path, _) =>
        {
            order.Add("dump:" + (await db.Database.GetAppliedMigrationsAsync()).Count());
            await File.WriteAllTextAsync(path, "dump");
        }, _clock, NullLogger.Instance);

        Assert.Equal(["dump:0"], order);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches(@"taxes_ua-pre-migrate-20261002T090000Z-from-empty-to-\d+_\w+\.dump$", file);
    }

    [Fact]
    public async Task A_failed_dump_stops_the_migration_and_leaves_the_schema_alone()
    {
        await using var db = await NewEmptyDatabase();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, _directory, 5, (_, _, _) => throw new IOException("disk full"), _clock, NullLogger.Instance));

        Assert.Contains("migrations were not run", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_dump_that_wrote_nothing_counts_as_failed()
    {
        await using var db = await NewEmptyDatabase();

        await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationDump.MigrateAsync(
            db, _directory, 5, (_, path, _) => File.WriteAllBytesAsync(path, []), _clock, NullLogger.Instance));

        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_database_with_nothing_pending_is_not_dumped()
    {
        await using var db = await NewEmptyDatabase();
        await db.Database.MigrateAsync();
        var calls = 0;

        await MigrationDump.MigrateAsync(db, _directory, 5, (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }, _clock, NullLogger.Instance);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_retry_of_the_same_migration_takes_no_second_dump()
    {
        await using var first = await NewEmptyDatabase();
        var calls = 0;
        DatabaseDump dump = (_, path, _) =>
        {
            calls++;
            return File.WriteAllTextAsync(path, "dump");
        };
        await MigrationDump.MigrateAsync(first, _directory, 5, dump, _clock, NullLogger.Instance);

        // The same pending set again, as after a crash between the dump and the migration.
        await using var retry = await NewEmptyDatabase();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await MigrationDump.MigrateAsync(retry, _directory, 5, dump, _clock, NullLogger.Instance);

        Assert.Equal(1, calls);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Only_the_newest_dumps_are_kept()
    {
        Directory.CreateDirectory(_directory);
        foreach (var day in new[] { "20260101", "20260102", "20260103" })
        {
            await File.WriteAllTextAsync(Path.Combine(_directory, $"{MigrationDump.FilePrefix}{day}T000000Z-x.dump"), "old");
        }

        await File.WriteAllTextAsync(Path.Combine(_directory, "unrelated.dump"), "mine");
        await using var db = await NewEmptyDatabase();

        await MigrationDump.MigrateAsync(db, _directory, 2, (_, path, _) => File.WriteAllTextAsync(path, "new"),
            _clock, NullLogger.Instance);

        var kept = Directory.GetFiles(_directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(3, kept.Count);
        Assert.Contains("unrelated.dump", kept);
        Assert.DoesNotContain(kept, name => name!.Contains("20260101", StringComparison.Ordinal));
        Assert.DoesNotContain(kept, name => name!.Contains("20260102", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_dump_directory_the_migration_still_runs()
    {
        await using var db = await NewEmptyDatabase();

        await MigrationDump.MigrateAsync(db, null, 5, (_, _, _) => throw new InvalidOperationException("no dump expected"),
            _clock, NullLogger.Instance);

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
