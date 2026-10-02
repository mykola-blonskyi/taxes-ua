using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Data;

public sealed class ChargeFullEsvMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string PreviousMigration = "20261001171537_AddEmailChannel";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task The_migration_moves_prorated_settings_to_the_full_month_and_logs_each_move()
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
                INSERT INTO "Settings" ("UserId", "DefaultCurrency", "EsvExempt", "EsvRegistrationMonthPolicy", "FopRegistrationDate", "Locale",
                    "PaymentMode", "ShiftTaxPaymentFromWeekend", "TaxPaymentCountsFromStatutoryDeclarationDate", "Theme", "WeekendDays")
                VALUES ('prorated-owner', 'UAH', false, 1, '2026-09-28', 'ru', 1, true, false, 'dark', ARRAY[0,6]),
                       ('full-month-owner', 'UAH', false, 0, NULL, 'uk', 0, true, true, 'system', ARRAY[6,0])
                """);
        }

        await using var after = Open();
        await after.Database.MigrateAsync();

        var policies = await after.Database
            .SqlQueryRaw<int>("""SELECT "EsvRegistrationMonthPolicy" AS "Value" FROM "Settings" ORDER BY "UserId" """)
            .ToListAsync();
        Assert.Equal([0, 0], policies);

        var entry = Assert.Single(await after.AuditLog.AsNoTracking().ToListAsync());
        Assert.Equal(
            ("prorated-owner", AuditedEntity.Settings, "prorated-owner", AuditAction.Update),
            (entry.UserId, entry.Entity, entry.EntityId, entry.Action));

        var (interceptorBefore, interceptorAfter) = await InterceptorEntryForTheSameChange();
        Assert.True(JsonNode.DeepEquals(AtThisMigration(interceptorBefore), Sorted(entry.Before!)), entry.Before);
        Assert.True(JsonNode.DeepEquals(AtThisMigration(interceptorAfter), Sorted(entry.After!)), entry.After);
    }

    // The same owner and change written through the API, so the interceptor's own snapshot is the reference.
    private async Task<(string Before, string After)> InterceptorEntryForTheSameChange()
    {
        using var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(login.Headers.Location)).StatusCode);

        var prorated = new SettingsRequest(
            FopRegistrationDate: new DateOnly(2026, 9, 28),
            PaymentMode: PaymentMode.MonthlyAdvance,
            EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.Prorated,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: false,
            ShiftTaxPaymentFromWeekend: true,
            WeekendDays: [DayOfWeek.Sunday, DayOfWeek.Saturday],
            Locale: "ru",
            Theme: "dark",
            DefaultCurrency: "UAH");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", prorated, Json)).StatusCode);
        var fullMonth = prorated with { EsvRegistrationMonthPolicy = EsvRegistrationMonthPolicy.FullMonth };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", fullMonth, Json)).StatusCode);

        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await database.AuditLog.AsNoTracking()
            .Where(row => row.Entity == AuditedEntity.Settings && row.Action == AuditAction.Update)
            .OrderByDescending(row => row.Id)
            .FirstAsync();
        return (entry.Before!, entry.After!);
    }

    // The Settings columns this migration saw. A later migration adds columns the interceptor now
    // snapshots too, and this migration must not change, so the reference is cut to what it knew.
    private static readonly string[] FieldsAtThisMigration =
    [
        "backOnGroup3FromQuarter", "backOnGroup3FromYear", "defaultCurrency", "esvExempt", "esvRegistrationMonthPolicy",
        "fopRegistrationDate", "locale", "paymentMode", "shiftTaxPaymentFromWeekend",
        "taxPaymentCountsFromStatutoryDeclarationDate", "theme", "weekendDays",
    ];

    private static JsonObject AtThisMigration(string json)
    {
        var fields = Sorted(json);
        foreach (var key in fields.Select(pair => pair.Key).Except(FieldsAtThisMigration).ToArray())
        {
            fields.Remove(key);
        }

        return fields;
    }

    private static JsonObject Sorted(string json) =>
        new(JsonNode.Parse(json)!.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
}
