using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Audit;

// ApiFixture is an IClassFixture, so this class owns its database. Every test filters the log by the
// id of the record it made, so the order xUnit runs them in does not matter.
public sealed class AuditLogTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Creating_editing_and_deleting_a_transaction_logs_three_entries_with_snapshots()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);

        var createdResponse = await client.PostAsJsonAsync(
            "/api/transactions",
            Transaction(new DateOnly(2021, 2, 3), 1_234_56, clientName: "Acme"),
            Json);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;

        var edited = await client.PutAsJsonAsync(
            $"/api/transactions/{created.Id}",
            Transaction(new DateOnly(2021, 2, 4), 2_000_00, clientName: "Globex", description: "fixed"),
            Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{created.Id}")).StatusCode);

        var log = await History(client, AuditedEntity.Transaction, created.Id.ToString());

        Assert.Equal([AuditAction.Delete, AuditAction.Update, AuditAction.Create], log.Select(entry => entry.Action));
        Assert.All(log, entry => Assert.Equal(created.Id.ToString(), entry.EntityId));
        Assert.All(log, entry => Assert.Equal(TimeSpan.Zero, entry.At.Offset));

        var (delete, update, create) = (log[0], log[1], log[2]);

        Assert.Null(create.Before);
        Assert.Equal(1_234_56, create.After!["amountUahKop"].GetInt64());
        Assert.Equal(JsonValueKind.Number, create.After["amountMinor"].ValueKind);
        Assert.Equal("2021-02-03", create.After["valueDate"].GetString());
        Assert.Equal("Income", create.After["kind"].GetString());
        Assert.Equal("Acme", create.After["clientName"].GetString());
        Assert.DoesNotContain("userId", create.After.Keys);
        Assert.DoesNotContain("clientId", create.After.Keys);
        Assert.DoesNotContain("id", create.After.Keys);

        Assert.Equal(1_234_56, update.Before!["amountUahKop"].GetInt64());
        Assert.Equal("Acme", update.Before["clientName"].GetString());
        Assert.Equal(2_000_00, update.After!["amountUahKop"].GetInt64());
        Assert.Equal("2021-02-04", update.After["valueDate"].GetString());
        Assert.Equal("Globex", update.After["clientName"].GetString());
        Assert.Equal("fixed", update.After["description"].GetString());

        Assert.Null(delete.After);
        Assert.Equal(2_000_00, delete.Before!["amountUahKop"].GetInt64());
        Assert.Equal("Globex", delete.Before["clientName"].GetString());
    }

    [Fact]
    public async Task Saving_a_transaction_unchanged_logs_nothing()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var body = Transaction(new DateOnly(2022, 5, 6), 500_00);
        var created = (await (await client.PostAsJsonAsync("/api/transactions", body, Json))
            .Content.ReadFromJsonAsync<TransactionResponse>(Json))!;

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/transactions/{created.Id}", body, Json)).StatusCode);

        var log = await History(client, AuditedEntity.Transaction, created.Id.ToString());
        Assert.Equal(AuditAction.Create, Assert.Single(log).Action);
    }

    [Fact]
    public async Task Creating_editing_and_deleting_a_payment_logs_three_entries()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var created = (await (await client.PostAsJsonAsync(
                "/api/payments", new PaymentRequest(new DateOnly(2033, 4, 15), PaymentKind.Esv, 190_234, 2033, 1, null, null), Json))
            .Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
        var edit = new PaymentRequest(new DateOnly(2033, 4, 15), PaymentKind.Esv, 200_000, 2033, 1, null, "late");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/payments/{created.Id}", edit, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/payments/{created.Id}")).StatusCode);

        var log = await History(client, AuditedEntity.BudgetPayment, created.Id.ToString());

        Assert.Equal([AuditAction.Delete, AuditAction.Update, AuditAction.Create], log.Select(entry => entry.Action));
        Assert.Equal(190_234, log[1].Before!["amountKop"].GetInt64());
        Assert.Equal(200_000, log[1].After!["amountKop"].GetInt64());
        Assert.Equal("Esv", log[1].After!["kind"].GetString());
        Assert.Equal(200_000, log[0].Before!["amountKop"].GetInt64());
    }

    [Fact]
    public async Task Changing_settings_is_logged_and_saving_them_unchanged_is_not()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);

        await PutSettings(client, new DateOnly(2026, 3, 1));
        await PutSettings(client, new DateOnly(2026, 4, 1));
        await PutSettings(client, new DateOnly(2026, 4, 1));

        var log = await History(client, AuditedEntity.Settings, id: null);

        Assert.Equal([AuditAction.Update, AuditAction.Create], log.Select(entry => entry.Action));
        Assert.Equal("2026-03-01", log[0].Before!["fopRegistrationDate"].GetString());
        Assert.Equal("2026-04-01", log[0].After!["fopRegistrationDate"].GetString());
        Assert.Equal(["Saturday", "Sunday"], log[0].After!["weekendDays"].EnumerateArray().Select(day => day.GetString()));
    }

    [Fact]
    public async Task Writing_verifying_and_cloning_a_tax_year_is_logged_under_the_user_who_did_it()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2091", TaxYear(800_000), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2091", TaxYear(850_000), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/tax-years/2091/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/tax-years/2091/clone-to/2092", null)).StatusCode);

        var log = await History(owner, AuditedEntity.TaxYearConfig, "2091");
        Assert.Equal([AuditAction.Update, AuditAction.Update, AuditAction.Create], log.Select(entry => entry.Action));
        Assert.Equal(800_000, log[1].Before!["minWageKop"].GetInt64());
        Assert.Equal(850_000, log[1].After!["minWageKop"].GetInt64());
        Assert.Equal(JsonValueKind.Null, log[0].Before!["verifiedAt"].ValueKind);
        Assert.Equal(JsonValueKind.String, log[0].After!["verifiedAt"].ValueKind);

        Assert.Equal(AuditAction.Create, Assert.Single(await History(owner, AuditedEntity.TaxYearConfig, "2092")).Action);
        Assert.Empty(await History(other, AuditedEntity.TaxYearConfig, "2091"));
    }

    [Fact]
    public async Task A_second_allowlisted_user_cannot_read_the_owners_log()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        var created = (await (await owner.PostAsJsonAsync("/api/transactions", Transaction(new DateOnly(2014, 1, 1), 100_00), Json))
            .Content.ReadFromJsonAsync<TransactionResponse>(Json))!;

        Assert.Single(await History(owner, AuditedEntity.Transaction, created.Id.ToString()));
        Assert.Empty(await History(other, AuditedEntity.Transaction, created.Id.ToString()));
        Assert.DoesNotContain(await History(other, entity: null, id: null), entry => entry.EntityId == created.Id.ToString());
    }

    [Fact]
    public async Task The_log_needs_a_session()
    {
        using var anonymous = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/audit")).StatusCode);
    }

    [Theory]
    [InlineData("""UPDATE "AuditLog" SET "EntityId" = 'x'""")]
    [InlineData("""DELETE FROM "AuditLog" """)]
    [InlineData("""TRUNCATE "AuditLog" """)]
    public async Task The_database_refuses_to_change_or_remove_an_entry(string sql)
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        await client.PostAsJsonAsync("/api/transactions", Transaction(new DateOnly(2015, 1, 1), 100_00), Json);

        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains("append-only", error.MessageText);
    }

    [Fact]
    public async Task A_save_that_carries_a_restore_summary_is_logged_by_that_summary_alone()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var restored = Guid.NewGuid();

        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await database.Users
                .Where(user => user.Email == ApiFixture.AllowedEmail)
                .Select(user => user.Id)
                .SingleAsync();

            database.Transactions.Add(new Transaction
            {
                Id = restored,
                UserId = userId,
                ValueDate = new DateOnly(2036, 1, 1),
                AmountMinor = 100_00,
                AmountUahKop = 100_00,
            });
            database.AuditLog.Add(AuditEntry.Restored(userId, DateTimeOffset.UtcNow, clients: 0, transactions: 1, budgetPayments: 0));
            await database.SaveChangesAsync();
        }

        Assert.Empty(await History(client, AuditedEntity.Transaction, restored.ToString()));
        var summary = (await History(client, AuditedEntity.Backup, id: null))[0];
        Assert.Equal(AuditAction.Restore, summary.Action);
        Assert.Null(summary.Before);
        Assert.Equal(1, summary.After!["transactions"].GetInt32());
    }

    // The next feature's table is logged only if someone adds it to the interceptor. This makes that a
    // decision the build asks for instead of one a reviewer has to remember.
    [Fact]
    public async Task Every_entity_is_either_audited_or_named_here_as_not_audited()
    {
        Type[] notAudited =
        [
            typeof(FxRate),
            typeof(AuditEntry),
            typeof(ApplicationUser),
            // The encrypted monobank token must never reach the change log (ADR-011).
            typeof(MonobankConnection),
            // A snapshot would carry the full IBAN onto the History screen, which shows every field,
            // while #75 masks it everywhere else.
            typeof(BankAccount),
            // A sync run's summary. The rows it imported each get their own Create entry.
            typeof(ImportBatch),
            // The bank's record of a sold currency, kept only to pair a sale; it is never a transaction
            // and the owner never sees or edits it.
            typeof(ForeignDebit),
            // A bank operation waiting for the owner's decision. Confirming writes the payment's own
            // Create or Update entry, which carries the operation id.
            typeof(BudgetPaymentCandidate),
            // A one-time link code and the bot's poll offset are the server's own runtime state; the
            // channel they set up is audited, and a code is a secret that must not reach any log.
            typeof(NotificationLinkCode),
            typeof(TelegramPollState),
            // A generated file is derived from rows that are audited themselves; its bytes are no
            // change the owner made.
            typeof(DeclarationFile),
            // What the reminder runs sent, written on every send; no owner decision is in it.
            typeof(SentReminder),
        ];

        await using var scope = fixture.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        var unclassified = model.GetEntityTypes()
            .Select(type => type.ClrType)
            .Where(type => type.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) != true)
            .Except(AuditSaveChangesInterceptor.AuditedTypes)
            .Except(notAudited);

        Assert.Empty(unclassified);
    }

    private static TransactionRequest Transaction(
        DateOnly valueDate, long amountKop, string? clientName = null, string? description = null) =>
        new(valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, clientName, null, description, null);

    private static TaxYearConfigRequest TaxYear(long minWageKop) => new(
        minWageKop, 500, 100, 2200, 1500, 1167, [80, 95], 20, 40, 10, 15, [], "a test source");

    private static async Task PutSettings(HttpClient client, DateOnly registered)
    {
        var request = new SettingsRequest(
            registered,
            PaymentMode.Quarterly,
            Api.Features.Settings.EsvRegistrationMonthPolicy.FullMonth,
            false,
            true,
            true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday],
            "uk",
            "system",
            "UAH");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);
    }

    private static async Task<AuditEntryResponse[]> History(HttpClient client, AuditedEntity? entity, string? id)
    {
        var query = new List<string>();
        if (entity is not null)
        {
            query.Add($"entity={entity}");
        }

        if (id is not null)
        {
            query.Add($"id={Uri.EscapeDataString(id)}");
        }

        var response = await client.GetAsync($"/api/audit?{string.Join('&', query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuditEntryResponse[]>(Json))!;
    }

    private async Task<HttpClient> SignIn(string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(login.Headers.Location)).StatusCode);
        return client;
    }
}
