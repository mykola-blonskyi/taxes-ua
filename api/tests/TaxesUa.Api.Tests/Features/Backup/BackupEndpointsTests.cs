using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Tests.Features.Backup;

// A restore replaces everything the owner has, so every test first puts the owner into a state of its
// own rather than relying on what an earlier test left.
public sealed class BackupEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Today = new(2031, 6, 1);

    private static readonly DateOnly NbuDate = new(2031, 3, 2);

    private const string Empty = """{"schemaVersion":1,"settings":null,"clients":[],"transactions":[],"budgetPayments":[]}""";

    private static readonly Guid ClientId = Guid.Parse("0f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid UahReceiptId = Guid.Parse("1f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid UsdReceiptId = Guid.Parse("1f0a0000-0000-0000-0000-000000000002");
    private static readonly Guid UsdRefundId = Guid.Parse("1f0a0000-0000-0000-0000-000000000003");
    private static readonly Guid TransferId = Guid.Parse("1f0a0000-0000-0000-0000-000000000004");
    private static readonly Guid QuarterPaymentId = Guid.Parse("2f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid MonthPaymentId = Guid.Parse("2f0a0000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Backup_then_wipe_then_restore_gives_back_the_same_file()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Empty);
        await SeedThroughTheApi(owner);

        var backup = await Backup(owner);
        var document = JsonSerializer.Deserialize<BackupDocument>(backup, Json)!;
        Assert.NotNull(document.Settings);
        Assert.Equal(2, document.Clients.Length);
        Assert.Equal(5, document.Transactions.Length);
        Assert.Contains(document.Transactions, row => row.RateSource == RateSource.Nbu);
        Assert.Contains(document.Transactions, row => row.RateSource == RateSource.Manual);
        Assert.Contains(document.Transactions, row => row.RefundsTransactionId is not null);
        Assert.Equal(2, document.BudgetPayments.Length);

        await Restore(owner, Empty);
        Assert.Equal(Normalized(Empty), Normalized(await Backup(owner)));

        var restored = await Restore(owner, backup);
        Assert.Equal(new RestoreResponse(2, 5, 2), restored);
        Assert.Equal(backup, await Backup(owner));

        var listed = await owner.GetFromJsonAsync<TransactionListResponse>("/api/transactions?year=2031", Json);
        var refund = Assert.Single(listed!.Items, item => item.Kind == TransactionKind.RefundToClient);
        Assert.Equal(document.Transactions.Single(row => row.RefundsTransactionId is not null).RefundsTransactionId, refund.RefundsReceipt?.Id);
    }

    [Fact]
    public async Task Restoring_another_owners_file_takes_fresh_ids_and_leaves_that_owner_alone()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await Restore(owner, Baseline().ToJsonString());
        var ownersBackup = await Backup(owner);

        await Restore(other, ownersBackup);

        Assert.Equal(ownersBackup, await Backup(owner));
        var original = JsonSerializer.Deserialize<BackupDocument>(ownersBackup, Json)!;
        var copy = JsonSerializer.Deserialize<BackupDocument>(await Backup(other), Json)!;
        Assert.Empty(original.Ids().Intersect(copy.Ids()));

        var ids = original.Ids().Zip(copy.Ids()).ToDictionary(pair => pair.First, pair => pair.Second);
        Guid? Map(Guid? id) => id is { } value ? ids[value] : null;
        var expected = original with
        {
            Clients = [.. original.Clients.Select(row => row with { Id = ids[row.Id] })],
            Transactions =
            [
                .. original.Transactions.Select(row => row with
                {
                    Id = ids[row.Id],
                    ClientId = Map(row.ClientId),
                    RefundsTransactionId = Map(row.RefundsTransactionId),
                }),
            ],
            BudgetPayments = [.. original.BudgetPayments.Select(row => row with { Id = ids[row.Id] })],
        };
        Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(copy, Json));
    }

    public static TheoryData<string, string?> InvalidFiles() => new()
    {
        { "link on a non-refund", "transactions[0].refundsTransactionId" },
        { "refund of a non-receipt", "transactions[2].refundsTransactionId" },
        { "over-refund", "transactions[2].refundsTransactionId" },
        { "currency mismatch", "transactions[2].refundsTransactionId" },
        { "link to a missing transaction", "transactions[2].refundsTransactionId" },
        { "quarter and month both set", "budgetPayments[0].periodQuarter" },
        { "month out of range", "budgetPayments[1].periodMonth" },
        { "duplicate transaction id", "transactions[1].id" },
        { "empty payment id", "budgetPayments[0].id" },
        { "unknown client", "transactions[0].clientId" },
        { "duplicate client name", "clients[1].name" },
        { "hryvnia amount off the rate", "transactions[1].amountUahKop" },
        { "UAH with a rate source", "transactions[0].rateE4" },
        { "manual rate with a date", "transactions[1].rateDate" },
        { "invalid settings", "settings.locale" },
        { "comma-joined currency", "transactions[1].currency" },
        { "comma-joined weekend day", "settings.weekendDays" },
        { "null row", "file" },
        { "refunds linking each other", "transactions[2].refundsTransactionId" },
        { "NBU rate dated after the transaction", "transactions[1].rateDate" },
        { "unknown field", null },
        { "missing field", null },
        { "numeric enum", null },
        { "schema version 2", null },
        { "no schema version", null },
        { "not JSON", null },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task An_invalid_file_is_rejected_and_changes_nothing(string name, string? errorKey)
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Baseline().ToJsonString());
        var before = await Backup(owner);

        var response = await Post(owner, Invalid(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (errorKey is not null)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = problem.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty(errorKey, out _), $"no error under {errorKey}: {errors}");
        }

        Assert.Equal(before, await Backup(owner));
    }

    [Fact]
    public async Task Two_restores_at_once_leave_exactly_the_files_rows()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Empty);
        var file = Baseline().ToJsonString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post(owner, file)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var document = JsonSerializer.Deserialize<BackupDocument>(await Backup(owner), Json)!;
        Assert.Equal(
            (2, 4, 2),
            (document.Clients.Length, document.Transactions.Length, document.BudgetPayments.Length));
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_before_it_is_read()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Baseline().ToJsonString());
        var before = await Backup(owner);

        var padded = Baseline().ToJsonString() + new string(' ', BackupEndpoints.MaxRestoreBytes);
        var response = await Post(owner, padded);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(before, await Backup(owner));
    }

    [Fact]
    public async Task A_restore_that_is_not_json_is_refused()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await owner.PostAsync(
            "/api/restore", new StringContent(Baseline().ToJsonString(), Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_the_routes_are_unauthorized()
    {
        await using var application = CreateApplication();
        using var client = ApiFixture.CreateClient(application);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/backup")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, Empty)).StatusCode);
    }

    // A column or an owner-scoped table added later fails here until the backup carries it or the
    // exclusion below names why it does not.
    [Fact]
    public async Task The_backup_covers_every_column_of_every_owner_table()
    {
        await using var application = CreateApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        var backedUp = new Dictionary<Type, Type>
        {
            [typeof(SettingsEntity)] = typeof(SettingsBackup),
            [typeof(Client)] = typeof(ClientBackup),
            [typeof(Transaction)] = typeof(TransactionBackup),
            [typeof(BudgetPayment)] = typeof(BudgetPaymentBackup),
        };
        Type[] sharedByEveryOwner = [typeof(TaxYearConfig), typeof(FxRate)];

        var featureTables = model.GetEntityTypes()
            .Select(type => type.ClrType)
            .Where(type => type.Namespace!.StartsWith("TaxesUa.Api.Features.", StringComparison.Ordinal)
                && type.Namespace != "TaxesUa.Api.Features.Auth")
            .ToHashSet();
        Assert.Equal(backedUp.Keys.Concat(sharedByEveryOwner).ToHashSet(), featureTables);

        foreach (var (entity, record) in backedUp)
        {
            var columns = model.FindEntityType(entity)!.GetProperties().Select(property => property.Name)
                .Where(name => name != "UserId");
            var carried = record.GetProperties().Select(property => property.Name).ToHashSet();
            Assert.All(columns, column => Assert.Contains(column, carried));
        }
    }

    private WebApplicationFactory<Program> CreateApplication() =>
        fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>
            {
                ["20310302"] = StubNbuHandler.Row("USD", NbuDate, "41.2345"),
            }),
            Today);

    private static async Task SeedThroughTheApi(HttpClient owner)
    {
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", new SettingsRequest(
            new DateOnly(2031, 1, 10),
            PaymentMode.MonthlyAdvance,
            EsvRegistrationMonthPolicy.Prorated,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: false,
            ShiftTaxPaymentFromWeekend: true,
            [DayOfWeek.Sunday, DayOfWeek.Saturday],
            "ru",
            "dark",
            "USD"), Json)).StatusCode);

        var orphan = await PostTransaction(owner, Transaction(new DateOnly(2031, 1, 20), 1_000, clientName: "Gone Ltd"));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{orphan.Id}")).StatusCode);

        await PostTransaction(owner, Transaction(
            new DateOnly(2031, 2, 1), 1_234_567, clientName: "Acme", invoiceNumber: "INV-1", description: "Консультації"));
        var usd = await PostTransaction(owner, Transaction(NbuDate, 100_000, Currency.USD, clientName: "Acme"));
        await PostTransaction(owner, Transaction(
            new DateOnly(2031, 3, 5), 25_000, Currency.USD, manualRateE4: 412_000,
            kind: TransactionKind.RefundToClient, refundsTransactionId: usd.Id));
        await PostTransaction(owner, Transaction(new DateOnly(2031, 3, 9), 20_000, Currency.EUR, manualRateE4: 450_000));
        await PostTransaction(owner, Transaction(
            new DateOnly(2031, 4, 1), 50_000, kind: TransactionKind.OwnTransfer, nonIncomeReason: "Own card"));

        foreach (var payment in new[]
                 {
                     new PaymentRequest(new DateOnly(2031, 4, 15), PaymentKind.Esv, 190_234, 2031, 1, null, "Q1 ESV"),
                     new PaymentRequest(new DateOnly(2031, 3, 15), PaymentKind.SingleTax, 61_700, 2031, null, 3, null),
                 })
        {
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/payments", payment, Json)).StatusCode);
        }
    }

    private static TransactionRequest Transaction(
        DateOnly valueDate,
        long amountMinor,
        Currency currency = Currency.UAH,
        int? manualRateE4 = null,
        TransactionKind kind = TransactionKind.Income,
        string? nonIncomeReason = null,
        string? clientName = null,
        string? invoiceNumber = null,
        string? description = null,
        Guid? refundsTransactionId = null) =>
        new(valueDate, amountMinor, currency, manualRateE4, kind, nonIncomeReason, clientName, invoiceNumber,
            description, refundsTransactionId);

    private static async Task<TransactionResponse> PostTransaction(HttpClient owner, TransactionRequest body)
    {
        var response = await owner.PostAsJsonAsync("/api/transactions", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    // Two receipts, a USD refund of part of the USD one, a transfer, and one payment of each period kind.
    private static JsonObject Baseline()
    {
        var created = new DateTimeOffset(2031, 5, 1, 9, 30, 0, TimeSpan.Zero);
        var document = new BackupDocument(
            BackupDocument.CurrentSchemaVersion,
            new SettingsBackup(
                new DateOnly(2031, 1, 1), PaymentMode.Quarterly, EsvRegistrationMonthPolicy.FullMonth, false, true,
                true, [DayOfWeek.Saturday, DayOfWeek.Sunday], "uk", "system", "UAH"),
            [new ClientBackup(ClientId, "Acme"), new ClientBackup(Guid.NewGuid(), "Beta")],
            [
                new TransactionBackup(UahReceiptId, new DateOnly(2031, 2, 1), 100_000, Currency.UAH, Money.RateScale,
                    null, null, 100_000, TransactionKind.Income, null, ClientId, null, null, null, created, created),
                new TransactionBackup(UsdReceiptId, new DateOnly(2031, 2, 2), 100_000, Currency.USD, 400_000,
                    null, RateSource.Manual, 4_000_000, TransactionKind.Income, null, null, null, "INV-2", null, created,
                    created),
                new TransactionBackup(UsdRefundId, new DateOnly(2031, 2, 3), 30_000, Currency.USD, 400_000,
                    null, RateSource.Manual, 1_200_000, TransactionKind.RefundToClient, null, null, UsdReceiptId, null,
                    null, created, created),
                new TransactionBackup(TransferId, new DateOnly(2031, 2, 4), 5_000, Currency.UAH, Money.RateScale,
                    null, null, 5_000, TransactionKind.OwnTransfer, "Own card", null, null, null, null, created, created),
            ],
            [
                new BudgetPaymentBackup(QuarterPaymentId, new DateOnly(2031, 4, 15), PaymentKind.Esv, 190_234, 2031, 1,
                    null, null, created, created),
                new BudgetPaymentBackup(MonthPaymentId, new DateOnly(2031, 3, 15), PaymentKind.SingleTax, 61_700, 2031,
                    null, 3, "March", created, created),
            ]);

        return JsonSerializer.SerializeToNode(document, Json)!.AsObject();
    }

    private static string Invalid(string name)
    {
        var file = Baseline();
        var transactions = file["transactions"]!.AsArray();
        var payments = file["budgetPayments"]!.AsArray();
        switch (name)
        {
            case "link on a non-refund":
                transactions[0]!["refundsTransactionId"] = UsdReceiptId;
                break;
            case "refund of a non-receipt":
                transactions[2]!["currency"] = "UAH";
                transactions[2]!["rateE4"] = Money.RateScale;
                transactions[2]!["rateSource"] = null;
                transactions[2]!["amountUahKop"] = 30_000;
                transactions[2]!["refundsTransactionId"] = TransferId;
                break;
            case "over-refund":
                transactions[2]!["amountMinor"] = 100_001;
                transactions[2]!["amountUahKop"] = Money.ToUahKop(100_001, 400_000);
                break;
            case "currency mismatch":
                transactions[2]!["currency"] = "EUR";
                break;
            case "link to a missing transaction":
                transactions[2]!["refundsTransactionId"] = Guid.NewGuid();
                break;
            case "quarter and month both set":
                payments[0]!["periodMonth"] = 1;
                break;
            case "month out of range":
                payments[1]!["periodMonth"] = 13;
                break;
            case "duplicate transaction id":
                transactions[1]!["id"] = UahReceiptId;
                break;
            case "empty payment id":
                payments[0]!["id"] = Guid.Empty;
                break;
            case "unknown client":
                transactions[0]!["clientId"] = Guid.NewGuid();
                break;
            case "duplicate client name":
                file["clients"]![1]!["name"] = " Acme ";
                break;
            case "hryvnia amount off the rate":
                transactions[1]!["amountUahKop"] = 4_000_001;
                break;
            case "UAH with a rate source":
                transactions[0]!["rateSource"] = "Manual";
                break;
            case "manual rate with a date":
                transactions[1]!["rateDate"] = "2031-02-02";
                break;
            case "invalid settings":
                file["settings"]!["locale"] = "en";
                break;
            case "comma-joined currency":
                transactions[1]!["currency"] = "USD, EUR";
                break;
            case "comma-joined weekend day":
                file["settings"]!["weekendDays"] = new JsonArray("Friday, Saturday");
                break;
            case "null row":
                file["clients"]!.AsArray().Add(null);
                break;
            case "refunds linking each other":
                transactions[1]!["kind"] = "RefundToClient";
                transactions[1]!["refundsTransactionId"] = UsdRefundId;
                break;
            case "NBU rate dated after the transaction":
                transactions[1]!["rateSource"] = "Nbu";
                transactions[1]!["rateDate"] = "2031-02-03";
                break;
            case "unknown field":
                transactions[0]!["amountKop"] = 1;
                break;
            case "missing field":
                transactions[0]!.AsObject().Remove("amountUahKop");
                break;
            case "numeric enum":
                transactions[0]!["kind"] = 0;
                break;
            case "schema version 2":
                file["schemaVersion"] = 2;
                break;
            case "no schema version":
                file.Remove("schemaVersion");
                break;
            case "not JSON":
                return "{\"schemaVersion\":1,";
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }

        return file.ToJsonString();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string file) =>
        client.PostAsync("/api/restore", new StringContent(file, Encoding.UTF8, "application/json"));

    private static async Task<RestoreResponse> Restore(HttpClient owner, string file)
    {
        var response = await Post(owner, file);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RestoreResponse>(Json))!;
    }

    private static async Task<string> Backup(HttpClient owner)
    {
        var response = await owner.GetAsync("/api/backup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("taxes-ua-backup-2031-06-01.json", response.Content.Headers.ContentDisposition?.FileNameStar);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Normalized(string json) => JsonNode.Parse(json)!.ToJsonString();
}
