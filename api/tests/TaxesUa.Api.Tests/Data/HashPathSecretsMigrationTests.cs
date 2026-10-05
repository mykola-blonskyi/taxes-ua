using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Tests.Features.Monobank;

namespace TaxesUa.Api.Tests.Data;

// #256 drops the plaintext feed and webhook secrets. The owner's calendar app and monobank keep the URLs they
// were given, so a database written before it must still answer them once migrated, and its bank token must
// end up bound to the owner.
public sealed class HashPathSecretsMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string PreviousMigration = "20261003094642_AddDeclarationContacts";

    private const string PublicBaseUrl = "https://taxes.example.test";

    private const string OwnerId = "legacy-owner";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Urls_issued_before_the_migration_keep_answering_and_only_their_hashes_stay()
    {
        var email = fixture.NewOwner();
        var feedSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var webhookSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "hash_path_secrets" };
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE hash_path_secrets", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var scope = fixture.CreateScope();
        var appOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        AppDbContext Open() => new(
            new DbContextOptionsBuilder<AppDbContext>(appOptions).UseNpgsql(connectionString.ConnectionString).Options);

        await using (var before = Open())
        {
            await before.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await Seed(before, email, feedSecret, webhookSecret);
            await before.Database.MigrateAsync();
            Assert.Empty(await before.Database.GetPendingMigrationsAsync());
        }

        var bank = new StubMonobankHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using (var application = fixture.CreateApplication(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString.ConnectionString);
            builder.UseSetting("Monobank:PublicBaseUrl", PublicBaseUrl);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => bank));
        }))
        {
            using var anonymous = ApiFixture.CreateClient(application);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/calendar/feed/{feedSecret}.ics")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/monobank/webhook/{webhookSecret}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsync($"/api/monobank/webhook/{webhookSecret}", null)).StatusCode);

            using var owner = await ApiFixture.SignIn(application, email);
            var feed = await owner.GetStringAsync("/api/calendar/feed");
            Assert.DoesNotContain(feedSecret, feed);
            Assert.Equal(
                new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero),
                JsonNode.Parse(feed)!["createdAt"]!.GetValue<DateTimeOffset>());

            // The registration monobank holds is the one migrated, so nothing has to be registered again.
            var connection = await owner.GetStringAsync("/api/monobank/connection");
            Assert.DoesNotContain(webhookSecret, connection);
            Assert.Equal("Registered", (await owner.GetFromJsonAsync<JsonObject>("/api/monobank/connection", Web))!["webhook"]!["state"]!.ToString());

            await using var appScope = application.Services.CreateAsyncScope();
            var encryptor = appScope.ServiceProvider.GetRequiredService<TokenEncryptor>();
            var stored = await appScope.ServiceProvider.GetRequiredService<AppDbContext>().MonobankConnections
                .AsNoTracking().SingleAsync(row => row.UserId == OwnerId);
            Assert.Equal(2, stored.EncryptedToken[0]);
            Assert.Equal("legacy-monobank-token", encryptor.Decrypt(stored.EncryptedToken, OwnerId));
            Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(stored.EncryptedToken, "another-owner"));
        }

        await using var after = Open();
        var row = await after.Database.SqlQueryRaw<string>(
            """SELECT concat_ws('|', f."SecretHash", c."WebhookSecretHash", c."WebhookBaseUrl") AS "Value" FROM "CalendarFeeds" f JOIN "MonobankConnections" c USING ("UserId") """)
            .SingleAsync();
        Assert.Equal(string.Join('|', Sha256(feedSecret), Sha256(webhookSecret), PublicBaseUrl), row);

        // Down keeps the schema usable: each hash stands in for the secret it can no longer give back.
        await after.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var rolledBack = await after.Database.SqlQueryRaw<string>(
            """SELECT concat_ws('|', f."Secret", c."WebhookSecret", coalesce(c."WebhookUrl", 'none')) AS "Value" FROM "CalendarFeeds" f JOIN "MonobankConnections" c USING ("UserId") """)
            .SingleAsync();
        Assert.Equal(string.Join('|', Sha256(feedSecret), Sha256(webhookSecret), "none"), rolledBack);
    }

    private static string Sha256(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static async Task Seed(AppDbContext database, string email, string feedSecret, string webhookSecret)
    {
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "AccessFailedCount",
                "EmailConfirmed", "LockoutEnabled", "PhoneNumberConfirmed", "TwoFactorEnabled", "SecurityStamp", "ConcurrencyStamp", "CreatedAt")
            VALUES ('legacy-owner', {0}, {1}, {0}, {1}, 0, true, false, false, false, 'STAMP', 'c0ffee', '2026-01-12T08:00:00Z')
            """,
            email,
            email.ToUpperInvariant());
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUserLogins" ("LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId")
            VALUES ('Development', {0}, 'Development', 'legacy-owner')
            """,
            $"development-key-for-{email}");
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "CalendarFeeds" ("UserId", "Secret", "CreatedAt")
            VALUES ('legacy-owner', {0}, '2026-09-30T21:00:00Z')
            """,
            feedSecret);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "MonobankConnections" ("UserId", "EncryptedToken", "MonobankClientId", "ConnectedAt", "WebhookSecret", "WebhookUrl")
            VALUES ('legacy-owner', {0}, 'legacy-client', '2026-09-30T21:00:00Z', {1}, {2})
            """,
            LegacyEncrypt("legacy-monobank-token"),
            webhookSecret,
            PublicBaseUrl + "/api/monobank/webhook/" + webhookSecret);
    }

    // The format written before #256: nonce, ciphertext and tag, with no version byte and no associated data.
    private static byte[] LegacyEncrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        nonce[0] = 0;
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var stored = new byte[12 + plain.Length + 16];
        nonce.CopyTo(stored, 0);
        using var aes = new AesGcm(Convert.FromBase64String(ApiFixture.MonobankTestKeyBase64), 16);
        aes.Encrypt(nonce, plain, stored.AsSpan(12, plain.Length), stored.AsSpan(12 + plain.Length, 16));
        return stored;
    }
}
