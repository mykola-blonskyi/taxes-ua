using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Data;

// A deploy runs the new migrations over the owner's real rows, which no other test does: they all start from
// an empty database at head. This one builds the schema as the previous release left it, fills it with an
// owner's year, migrates to head with EF's MigrateAsync (Program.cs's dump-then-migrate path is MigrationDumpTests' job) and reads the API over what the migrations kept.
public sealed class UpgradeFromPreviousReleaseTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // Move this forward, and extend the seed, whenever a release's migrations rewrite existing rows.
    // The last migration before ChargeFullEsvForRegistrationMonth, TrackGroup3Status and everything after
    // them. Later migrations on main only add to the ones that follow, so this stays a real "older" schema.
    private const string PreviousRelease = "20261001171537_AddEmailChannel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_database_from_the_previous_release_migrates_and_the_api_reads_the_owners_year()
    {
        var owner = fixture.NewOwner();
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "upgrade_previous_release" };
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE upgrade_previous_release", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using (var scope = fixture.CreateScope())
        {
            var appOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
            await using var database = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>(appOptions).UseNpgsql(connectionString.ConnectionString).Options);

            await database.GetService<IMigrator>().MigrateAsync(PreviousRelease);
            await Seed(database, owner);

            // Everything the owner has is still there once the schema is at head.
            await database.Database.MigrateAsync();
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            Assert.Equal(3, await database.Transactions.IgnoreQueryFilters().CountAsync(row => row.UserId == "upgrade-owner"));
        }

        var today = new DateOnly(2026, 10, 15);
        await using var application = fixture.CreateApplication(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString.ConnectionString);
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(
                new FakeTimeProvider(new DateTimeOffset(today, new TimeOnly(10, 0), TimeSpan.Zero))));
        });
        using var client = await ApiFixture.SignIn(application, owner);

        var settings = (await client.GetFromJsonAsync<JsonObject>("/api/settings", Web))!;
        Assert.Equal("FullMonth", settings["esvRegistrationMonthPolicy"]!.GetValue<string>());
        Assert.Equal("2026-01-12", settings["fopRegistrationDate"]!.GetValue<string>());

        // 250 000.00 UAH and 3 000.00 USD at 41.2345 make 373 703.50 UAH in the first quarter; the own
        // transfer counts for nothing.
        var transactions = (await client.GetFromJsonAsync<JsonObject>("/api/transactions?year=2026", Web))!;
        Assert.Equal(37_370_350, transactions["totalIncomeKop"]!.GetValue<long>());
        Assert.Equal(3, transactions["items"]!.AsArray().Count);

        var dashboard = (await client.GetFromJsonAsync<JsonObject>("/api/dashboard", Web))!;
        Assert.Equal("2026-10-15", dashboard["today"]!.GetValue<string>());
        Assert.Equal("Pay", dashboard["nextStep"]!["state"]!.GetValue<string>());
        var due = dashboard["nextStep"]!["now"]!.AsArray();
        Assert.Equal(
            [("SingleTax", 1_868_518L), ("MilitaryLevy", 373_704L), ("Esv", 570_702L)],
            due.Select(row => (row!["kind"]!.GetValue<string>(), row["amountKop"]!.GetValue<long>())).ToArray());
        Assert.Equal(37_370_350, dashboard["limit"]!["incomeKop"]!.GetValue<long>());
        Assert.Equal("2026-01-12", dashboard["group3"]!["group3Start"]!.GetValue<string>());

        var quarter = (await client.GetFromJsonAsync<JsonObject>("/api/periods/2026", Web))!["quarters"]![0]!;
        Assert.Equal(
            (37_370_350L, 1_868_518L, 373_704L, 570_702L),
            (quarter["incomeKop"]!.GetValue<long>(), quarter["singleTaxKop"]!.GetValue<long>(),
                quarter["militaryLevyKop"]!.GetValue<long>(), quarter["esvKop"]!.GetValue<long>()));
        Assert.Equal(570_702, quarter["obligations"]!["esv"]!["paidKop"]!.GetValue<long>());
        Assert.Equal("Done", quarter["obligations"]!["esv"]!["status"]!.GetValue<string>());

        var declaration = (await client.GetFromJsonAsync<JsonObject>("/api/declarations/2026/1", Web))!;
        var figures = declaration["figures"]!;
        Assert.Equal(
            (37_370_350L, 1_868_518L, 373_704L),
            (figures["incomeKop"]!.GetValue<long>(), figures["singleTaxKop"]!.GetValue<long>(), figures["militaryLevyKop"]!.GetValue<long>()));
        Assert.Null(declaration["filed"]);
    }

    // An owner who registered as a FOP in January 2026 and, in 2026, billed one UAH client and one USD client,
    // moved some money between their own cards and paid the first quarter's ESV.
    private static async Task Seed(AppDbContext database, string email)
    {
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "AccessFailedCount",
                "EmailConfirmed", "LockoutEnabled", "PhoneNumberConfirmed", "TwoFactorEnabled", "SecurityStamp", "ConcurrencyStamp", "CreatedAt")
            VALUES ('upgrade-owner', {0}, {1}, {0}, {1}, 0, true, false, false, false, 'STAMP-FROM-THE-PREVIOUS-RELEASE', 'c0ffee', '2026-01-12T08:00:00Z')
            """,
            email,
            email.ToUpperInvariant());

        // The owner has signed in before, so the sign-in finds the account by its login.
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUserLogins" ("LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId")
            VALUES ('Development', {0}, 'Development', 'upgrade-owner')
            """,
            $"development-key-for-{email}");
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Settings" ("UserId", "DefaultCurrency", "EsvExempt", "EsvRegistrationMonthPolicy", "FopRegistrationDate", "Locale",
                "PaymentMode", "ShiftTaxPaymentFromWeekend", "TaxPaymentCountsFromStatutoryDeclarationDate", "Theme", "WeekendDays")
            VALUES ('upgrade-owner', 'UAH', false, 1, '2026-01-12', 'uk', 0, true, true, 'system', ARRAY[6,0])
            """);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Clients" ("Id", "UserId", "Name")
            VALUES ('c0000000-0000-0000-0000-000000000001', 'upgrade-owner', 'Acme GmbH')
            """);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Transactions" ("Id", "UserId", "ValueDate", "AmountMinor", "Currency", "RateE4", "RateDate", "RateSource",
                "AmountUahKop", "Kind", "ClientId", "ReviewStatus", "CreatedAt", "UpdatedAt")
            VALUES
              ('a0000000-0000-0000-0000-000000000001', 'upgrade-owner', '2026-02-10', 25000000, 0, 10000, NULL, NULL,
                25000000, 0, NULL, 0, '2026-02-10T09:00:00Z', '2026-02-10T09:00:00Z'),
              ('a0000000-0000-0000-0000-000000000002', 'upgrade-owner', '2026-03-02', 300000, 1, 412345, '2026-03-02', 0,
                12370350, 0, 'c0000000-0000-0000-0000-000000000001', 0, '2026-03-02T09:00:00Z', '2026-03-02T09:00:00Z'),
              ('a0000000-0000-0000-0000-000000000003', 'upgrade-owner', '2026-03-05', 500000, 0, 10000, NULL, NULL,
                500000, 2, NULL, 0, '2026-03-05T09:00:00Z', '2026-03-05T09:00:00Z')
            """);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BudgetPayments" ("Id", "UserId", "PaidOn", "Kind", "AmountKop", "PeriodYear", "PeriodQuarter", "PeriodMonth", "CreatedAt", "UpdatedAt")
            VALUES ('b0000000-0000-0000-0000-000000000001', 'upgrade-owner', '2026-04-15', 2, 570702, 2026, 1, NULL,
                '2026-04-15T09:00:00Z', '2026-04-15T09:00:00Z')
            """);
    }
}
