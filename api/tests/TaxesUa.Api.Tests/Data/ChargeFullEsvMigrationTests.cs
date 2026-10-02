using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Data;

public sealed class ChargeFullEsvMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string PreviousMigration = "20261001171537_AddEmailChannel";

    [Fact]
    public async Task The_migration_moves_prorated_settings_to_the_full_month_and_leaves_the_rest()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "esv_migration" };
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE esv_migration", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var scope = fixture.CreateScope();
        var appOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        AppDbContext Open() => new(
            new DbContextOptionsBuilder<AppDbContext>(appOptions).UseNpgsql(connectionString.ConnectionString).Options);

        await using (var before = Open())
        {
            await before.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "AspNetUsers" ("Id", "AccessFailedCount", "EmailConfirmed", "LockoutEnabled", "PhoneNumberConfirmed", "TwoFactorEnabled", "CreatedAt")
                VALUES ('prorated-owner', 0, false, false, false, false, now()), ('full-month-owner', 0, false, false, false, false, now())
                """);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Settings" ("UserId", "DefaultCurrency", "EsvExempt", "EsvRegistrationMonthPolicy", "Locale",
                    "PaymentMode", "ShiftTaxPaymentFromWeekend", "TaxPaymentCountsFromStatutoryDeclarationDate", "Theme", "WeekendDays")
                VALUES ('prorated-owner', 'UAH', false, 1, 'uk', 0, true, true, 'system', ARRAY[6,0]),
                       ('full-month-owner', 'UAH', false, 0, 'uk', 0, true, true, 'system', ARRAY[6,0])
                """);
        }

        await using var after = Open();
        await after.Database.MigrateAsync();

        var policies = await after.Database
            .SqlQueryRaw<int>("""SELECT "EsvRegistrationMonthPolicy" AS "Value" FROM "Settings" ORDER BY "UserId" """)
            .ToListAsync();
        Assert.Equal([0, 0], policies);
    }
}
