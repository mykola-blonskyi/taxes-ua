using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Calendar;
using TaxesUa.Api.Features.Clients;
using TaxesUa.Api.Features.DatabaseBackups;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Api.Tests.Features.Settings;
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

    private const string Empty =
        """{"schemaVersion":19,"settings":null,"clients":[],"transactions":[],"budgetPayments":[],"bankAccounts":[],"importBatches":[],"budgetPaymentCandidates":[],"invoicingDetails":null,"invoices":[],"declarationDetails":null,"declarationFilings":[],"declarationFiles":[],"treasuryAccounts":[],"notificationChannels":[],"reserveJar":null}""";

    private static readonly Guid ClientId = Guid.Parse("0f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid UahReceiptId = Guid.Parse("1f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid UsdReceiptId = Guid.Parse("1f0a0000-0000-0000-0000-000000000002");
    private static readonly Guid UsdRefundId = Guid.Parse("1f0a0000-0000-0000-0000-000000000003");
    private static readonly Guid TransferId = Guid.Parse("1f0a0000-0000-0000-0000-000000000004");
    private static readonly Guid QuarterPaymentId = Guid.Parse("2f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid MonthPaymentId = Guid.Parse("2f0a0000-0000-0000-0000-000000000002");
    private static readonly Guid IssuedInvoiceId = Guid.Parse("3f0a0000-0000-0000-0000-000000000001");
    private static readonly Guid DraftInvoiceId = Guid.Parse("3f0a0000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Backup_then_wipe_then_restore_gives_back_the_same_file()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
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
        Assert.Equal(["62.01"], document.DeclarationDetails!.KvedCodes);
        Assert.Equal(PaymentKind.MilitaryLevy, Assert.Single(document.TreasuryAccounts).Kind);

        await Wipe(owner);
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
            Invoices = [.. original.Invoices.Select(row => row with { Id = ids[row.Id], ClientId = ids[row.ClientId] })],
        };
        Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(copy, Json));
    }

    [Fact]
    public async Task Prorated_restores_as_the_owners_choice()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var file = Baseline();
        file["settings"]!["esvRegistrationMonthPolicy"] = "Prorated";

        await Restore(owner, file.ToJsonString());

        var settings = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal("Prorated", settings!.EsvRegistrationMonthPolicy.ToString());
    }

    [Fact]
    public async Task A_restore_brings_back_the_invoicing_details_and_the_signature_image()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());

        var details = await owner.GetFromJsonAsync<InvoicingDetailsResponse>("/api/settings/invoicing", Json);
        Assert.Equal("1234567890", details!.Rnokpp);
        Assert.Equal("Комісії сплачує платник.", details.FeesClauseUk);
        Assert.Equal([Currency.USD, Currency.EUR], details.PaymentDetails.Select(row => row.Currency));
        Assert.True(details.HasSignature);
        var image = await owner.GetAsync("/api/settings/invoicing/signature");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InvoicingTestData.Png, await image.Content.ReadAsByteArrayAsync());
        var backup = JsonNode.Parse(await Backup(owner))!.AsObject();
        Assert.Equal(Convert.ToBase64String(InvoicingTestData.Png), backup["invoicingDetails"]!["signatureImage"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_restore_brings_back_the_return_to_group_3()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());
        var restored = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);

        Assert.Equal(new YearQuarter(2032, 2), restored!.BackOnGroup3From);
    }

    [Fact]
    public async Task A_restore_brings_back_the_telegram_channel()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());
        var restored = (await owner.GetFromJsonAsync<JsonArray>("/api/notifications/channels"))!.Single(channel => channel!["kind"]!.GetValue<string>() == "Telegram")!;

        Assert.True(restored["linked"]!.GetValue<bool>());
        Assert.False(restored["confirmed"]!.GetValue<bool>());
        Assert.False(restored["enabled"]!.GetValue<bool>());
        Assert.Null(restored["lastDeliveryAt"]);
    }

    [Fact]
    public async Task A_restore_brings_back_the_reserve_jar_with_the_time_of_its_balance_and_a_file_without_one_clears_it()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());
        var restored = await owner.GetFromJsonAsync<ReserveJarStateResponse>("/api/monobank/reserve-jar", Json);
        var backup = JsonSerializer.Deserialize<BackupDocument>(await Backup(owner), Json)!;
        var withoutJar = Baseline();
        withoutJar.Remove("reserveJar");
        await Restore(owner, withoutJar.ToJsonString());
        var upgraded = await owner.GetFromJsonAsync<ReserveJarStateResponse>("/api/monobank/reserve-jar", Json);

        var expected = new ReserveJarBackup("jar-taxes", "На податки", 12_345_00, new DateTimeOffset(2031, 5, 1, 12, 30, 0, TimeSpan.Zero));
        Assert.Equal(expected, backup.ReserveJar);
        Assert.Equal(
            ("jar-taxes", "На податки", 12_345_00L, expected.FetchedAt),
            (restored!.Jar!.JarId, restored.Jar.Title, restored.Jar.BalanceKop, restored.Jar.FetchedAt));
        Assert.Null(upgraded!.Jar);
    }

    [Fact]
    public async Task A_restore_brings_back_every_channel_unconfirmed_and_off_and_the_file_holds_no_link()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        var file = Baseline();
        file["notificationChannels"]!.AsArray().Add(JsonNode.Parse(
            """{"kind":"Email","address":"owner@mail.test","enabled":true,"linkedAt":"2031-03-01T08:00:00+00:00","confirmedAt":"2031-03-01T08:05:00+00:00"}"""));

        await Restore(owner, file.ToJsonString());
        var channels = (await owner.GetFromJsonAsync<JsonArray>("/api/notifications/channels"))!;
        var email = channels.Single(channel => channel!["kind"]!.GetValue<string>() == "Email")!;
        var telegram = channels.Single(channel => channel!["kind"]!.GetValue<string>() == "Telegram")!;
        var backup = await Backup(owner);

        Assert.True(email["linked"]!.GetValue<bool>());
        Assert.False(email["confirmed"]!.GetValue<bool>());
        Assert.False(email["enabled"]!.GetValue<bool>());
        Assert.Equal("owner@mail.test", email["address"]!.GetValue<string>());
        Assert.False(telegram["confirmed"]!.GetValue<bool>());
        Assert.False(telegram["enabled"]!.GetValue<bool>());
        var saved = JsonNode.Parse(backup)!["notificationChannels"]!.AsArray()
            .Single(channel => channel!["kind"]!.GetValue<string>() == "Email")!;
        Assert.Null(saved["confirmedAt"]);
        Assert.False(saved["enabled"]!.GetValue<bool>());
        Assert.DoesNotContain("confirmEmail", backup);
        Assert.DoesNotContain("token", backup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_restore_brings_back_the_dps_status()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());
        var restored = await owner.GetFromJsonAsync<DpsStatusResponse>("/api/settings/dps-status", Json);

        Assert.Equal(
            ((DateOnly?)null, new Group3ConfirmationDto(new DateOnly(2031, 1, 5), "9123456789"), true, true, false),
            (restored!.Group3Since, restored.Confirmation, restored.FopRegistered, restored.EsvRegistered, restored.AccountsRegistered));
    }

    // A tampered file could name any chat id. Reminders must not follow it until the owner presses Start
    // in Telegram again, and the file the restore leaves behind restores in turn.
    [Fact]
    public async Task A_file_naming_a_confirmed_and_enabled_telegram_chat_restores_it_unconfirmed_and_off()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        var file = Baseline();
        var channel = file["notificationChannels"]![0]!.AsObject();
        channel["address"] = "666666";
        channel["enabled"] = true;
        channel["confirmedAt"] = channel["linkedAt"]!.DeepClone();

        await Restore(owner, file.ToJsonString());
        var telegram = (await owner.GetFromJsonAsync<JsonArray>("/api/notifications/channels"))!
            .Single(row => row!["kind"]!.GetValue<string>() == "Telegram")!;
        var saved = await Backup(owner);
        await Restore(owner, saved);
        var again = JsonNode.Parse(await Backup(owner))!["notificationChannels"]![0]!;

        Assert.False(telegram["confirmed"]!.GetValue<bool>());
        Assert.False(telegram["enabled"]!.GetValue<bool>());
        Assert.Null(JsonNode.Parse(saved)!["notificationChannels"]![0]!["confirmedAt"]);
        Assert.False(again["enabled"]!.GetValue<bool>());
        Assert.Null(again["confirmedAt"]);
    }

    [Fact]
    public async Task A_restore_brings_back_the_declaration_details_and_the_filed_marks()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());

        var details = await owner.GetFromJsonAsync<DeclarationDetailsResponse>("/api/settings/declaration", Json);
        Assert.Equal(
            (26, 5, "ГУ ДПС у м. Києві", "62.01 63.11", "Київ, вул. Тестова 1", 0),
            (details!.TaxOfficeRegion, details.TaxOfficeDistrict, details.TaxOfficeName, string.Join(' ', details.KvedCodes),
                details.Address, details.MissingDetails.Length));
        var backup = JsonSerializer.Deserialize<BackupDocument>(await Backup(owner), Json)!;
        Assert.Equal(
            [(2030, 4, DeclarationType.Reporting), (2031, 1, DeclarationType.Clarifying)],
            backup.DeclarationFilings.Select(filing => (filing.Year, filing.Quarter, filing.Type)));
    }

    [Fact]
    public async Task A_restore_keeps_a_KVED_code_the_classifier_does_not_know_and_the_readiness_reports_it()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        var file = Baseline();
        file["declarationDetails"]!["kvedCodes"] = new JsonArray("62.01", "12.34");

        await Restore(owner, file.ToJsonString());

        var details = await owner.GetFromJsonAsync<DeclarationDetailsResponse>("/api/settings/declaration", Json);
        Assert.Equal(["62.01", "12.34"], details!.KvedCodes);
        Assert.Equal(["12.34"], details.UnknownKvedCodes);
        Assert.Empty(details.MissingDetails);
        Assert.Contains("12.34", JsonNode.Parse(await Backup(owner))!["declarationDetails"]!["kvedCodes"]!.ToJsonString());
    }

    [Fact]
    public async Task A_restore_with_a_malformed_KVED_code_is_refused_as_a_format_error()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var file = Baseline();
        file["declarationDetails"]!["kvedCodes"] = new JsonArray("62.01", "6201");

        var response = await Post(owner, file.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        ProblemAssert.FieldIs(problem.RootElement, "declarationDetails.kvedCodes[1]", ProblemCodes.KvedFormatInvalid);
    }

    [Fact]
    public async Task A_restore_brings_back_the_declaration_files_byte_for_byte()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());

        await using var scope = fixture.CreateScope();
        var files = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeclarationFiles.AsNoTracking()
            .OrderBy(row => row.Type)
            .ToListAsync();
        Assert.Equal(
            [
                (DeclarationType.Reporting, "26051234567890F0103309100000000120320312605.xml", DeclarationFileBytes,
                    "26051234567890F0133109100000000120320312605.xml", AnnexFileBytes),
                (DeclarationType.Clarifying, "26051234567890F0103309300000000120320312605.xml", new byte[] { 0x3C, 0x00, 0xFF },
                    null, null),
            ],
            files.Select(row => (row.Type, row.FileName, row.Content, row.AnnexFileName, row.AnnexContent)));
        Assert.Equal(new DateTimeOffset(2031, 5, 1, 9, 30, 0, TimeSpan.Zero), files[0].GeneratedAt);
        var backup = JsonSerializer.Deserialize<BackupDocument>(await Backup(owner), Json)!;
        Assert.Equal(DeclarationFileBytes, backup.DeclarationFiles[0].Content);
        Assert.Equal(AnnexFileBytes, backup.DeclarationFiles[0].AnnexContent);
        Assert.Null(backup.DeclarationFiles[1].AnnexContent);
    }

    [Fact]
    public async Task A_restore_brings_back_the_end_of_each_treasury_account()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Baseline().ToJsonString());

        var kept = JsonNode.Parse(await Backup(owner))!["treasuryAccounts"]!.AsArray();
        Assert.Equal(
            ("2031-12-31", "2032-06-30"),
            (kept[0]!["manualValidUntil"]!.GetValue<string>(), kept[0]!["learnedValidUntil"]!.GetValue<string>()));
        var single = (await owner.GetFromJsonAsync<JsonElement>("/api/settings/treasury-accounts", Json)).EnumerateArray().First();
        Assert.Equal("2031-12-31", single.GetProperty("validUntil").GetString());
    }

    [Fact]
    public async Task A_restore_brings_back_treasury_accounts_with_their_source_and_notice()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await Restore(owner, Baseline().ToJsonString());

        var accounts = (await owner.GetFromJsonAsync<JsonElement>("/api/settings/treasury-accounts", Json)).EnumerateArray().ToArray();

        Assert.Equal(
            [("SingleTax", "Manual", TreasuryIban, true), ("MilitaryLevy", "None", null, false), ("Esv", "Learned", LearnedTreasuryIban, false)],
            accounts.Select(row => (
                row.GetProperty("kind").GetString()!,
                row.GetProperty("source").GetString()!,
                row.GetProperty("iban").GetString(),
                row.GetProperty("notice").ValueKind == JsonValueKind.Object)));
        Assert.Equal(["recipientName", "recipientCode"], accounts[2].GetProperty("missing").EnumerateArray().Select(item => item.GetString()));
        await Restore(owner, Baseline().ToJsonString());
        Assert.Equal(2, JsonNode.Parse(await Backup(owner))!["treasuryAccounts"]!.AsArray().Count);

        await Restore(other, Empty);
        Assert.Equal(
            ["None", "None", "None"],
            (await other.GetFromJsonAsync<JsonElement>("/api/settings/treasury-accounts", Json)).EnumerateArray()
                .Select(row => row.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task A_restore_brings_back_issued_and_draft_invoices_with_their_numbers()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);

        await Restore(owner, Baseline().ToJsonString());

        var invoices = await owner.GetFromJsonAsync<InvoiceSummary[]>("/api/invoices", Json);
        Assert.Equal(
            [(DraftInvoiceId, InvoiceStatus.Draft, (string?)null, 500_00L), (IssuedInvoiceId, InvoiceStatus.Issued, "2031-001", 500_00L)],
            invoices!.Select(row => (row.Id, row.Status, row.Number, row.TotalMinor)));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/invoices/{IssuedInvoiceId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/clients/{ClientId}")).StatusCode);
    }

    [Fact]
    public async Task A_restore_that_would_drop_an_issued_invoice_is_refused_and_changes_nothing()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        try
        {
            await Restore(owner, Baseline().ToJsonString());
            var older = await Backup(owner);

            var draft = await CreateDraftInvoice(owner);
            var issued = await IssueInvoice(owner, draft);
            Assert.Equal("2031-002", issued.Number);
            var newer = await Backup(owner);

            var response = await Post(owner, older);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("backup_missing_invoices", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(["2031-002"], problem.RootElement.GetProperty("missingInvoices")
                .EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(newer, await Backup(owner));

            var next = await IssueInvoice(owner, await CreateDraftInvoice(owner));
            Assert.Equal("2031-003", next.Number);

            var withAll = await Backup(owner);
            Assert.Equal(new RestoreResponse(2, 4, 2), await Restore(owner, withAll));
        }
        finally
        {
            await Wipe(owner);
        }
    }

    [Fact]
    public async Task A_restore_names_every_dropped_cancelled_or_issued_number_but_may_drop_drafts()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        try
        {
            await Restore(owner, Baseline().ToJsonString());
            var older = await Backup(owner);
            var second = await IssueInvoice(owner, await CreateDraftInvoice(owner));
            await IssueInvoice(owner, await CreateDraftInvoice(owner));
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
                $"/api/invoices/{second.Id}/cancel", new CancelInvoiceRequest("Duplicate"), Json)).StatusCode);

            var response = await Post(owner, older);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("backup_missing_invoices", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(["2031-002", "2031-003"], problem.RootElement.GetProperty("missingInvoices")
                .EnumerateArray().Select(item => item.GetString()));

            var withoutDrafts = JsonNode.Parse(await Backup(owner))!.AsObject();
            withoutDrafts["invoices"] = new JsonArray(withoutDrafts["invoices"]!.AsArray()
                .Where(row => row!["status"]!.GetValue<string>() != "Draft").Select(row => row!.DeepClone()).ToArray());
            Assert.Equal(HttpStatusCode.OK, (await Post(owner, withoutDrafts.ToJsonString())).StatusCode);
        }
        finally
        {
            await Wipe(owner);
        }
    }

    private async Task<InvoiceResponse> CreateDraftInvoice(HttpClient owner)
    {
        var response = await owner.PostAsJsonAsync("/api/invoices", new InvoiceRequest(
            ClientId, Today, Today.AddDays(14), Currency.EUR,
            [new InvoiceLineRequest("Consulting", "Консультації", InvoiceUnit.Hour, 1_000, 100_00)]), Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
    }

    private static async Task<InvoiceResponse> IssueInvoice(HttpClient owner, InvoiceResponse draft)
    {
        var response = await owner.PostAsync($"/api/invoices/{draft.Id}/issue", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
    }

    [Fact]
    public async Task Client_details_survive_a_backup_and_restore()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Baseline().ToJsonString());

        var backup = JsonNode.Parse(await Backup(owner))!.AsObject();
        var acme = backup["clients"]!.AsArray().OfType<JsonObject>().Single(row => row["name"]!.GetValue<string>() == "Acme");
        Assert.Equal("DE", acme["country"]!.GetValue<string>());
        Assert.Equal("EUR", acme["defaultCurrency"]!.GetValue<string>());
        Assert.Equal("Net 14", acme["notes"]!.GetValue<string>());

        var clients = await owner.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json);
        var listed = Assert.Single(clients!, row => row.Name == "Acme");
        Assert.Equal(new ClientResponse(
            ClientId, "Acme", "1 Main St, Berlin", "DE", "DE123456789", "ap@acme.example", Currency.EUR, "Net 14", 1), listed);
    }

    [Fact]
    public async Task A_restore_logs_exactly_one_summary_entry_and_no_per_row_entries()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
        await SeedThroughTheApi(owner);
        var backup = await Backup(owner);
        var before = await History(owner, entity: null, id: null);

        var restored = await Restore(owner, backup);

        var log = await History(owner, entity: null, id: null);
        var newEntries = log.ExceptBy(before.Select(entry => entry.Id), entry => entry.Id).ToArray();
        var summary = Assert.Single(newEntries);
        Assert.Equal(AuditedEntity.Backup, summary.Entity);
        Assert.Equal(AuditAction.Restore, summary.Action);
        Assert.Null(summary.Before);
        Assert.Equal(restored.Clients, summary.After!["clients"].GetInt32());
        Assert.Equal(restored.Transactions, summary.After["transactions"].GetInt32());
        Assert.Equal(restored.BudgetPayments, summary.After["budgetPayments"].GetInt32());
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
        { "invoicing details with a bad IBAN", "invoicingDetails.paymentDetails[0].iban" },
        { "signature that is not base64", "invoicingDetails.signatureImage" },
        { "signature that is not the declared type", "invoicingDetails.signatureImage" },
        { "signature without a type", "invoicingDetails.signatureImage" },
        { "signature over the size cap", "invoicingDetails.signatureImage" },
        { "invoice of an unknown client", "invoices[0].clientId" },
        { "invoice numbered in another year", "invoices[0].status" },
        { "draft with a number", "invoices[1].status" },
        { "repeated invoice number", "invoices[1].numberSequence" },
        { "invoice line with no quantity", "invoices[0].lines[0].quantityThousandths" },
        // StrictEnumJsonConverter rejects any comma-joined string outright, so these now fail while the
        // file is parsed, before Validate ever sees a currency or weekendDays value to report a key
        // for, the same way "numeric enum" below does.
        { "comma-joined currency", null },
        { "comma-joined weekend day", null },
        // Income is 0 and RefundToClient is 1, so this is the exact string #53 reported: it used to
        // silently store as RefundToClient instead of being rejected.
        { "comma-joined transaction kind", null },
        { "NUL in a client name", "clients[0].name" },
        { "null row", "file" },
        { "refunds linking each other", "transactions[2].refundsTransactionId" },
        { "NBU rate dated after the transaction", "transactions[1].rateDate" },
        { "future-dated transaction", "transactions[0].valueDate" },
        { "dismissed typed row", "transactions[0].reviewStatus" },
        { "dismissed import keeping its refund link", "transactions[2].refundsTransactionId" },
        { "refund of a dismissed import", "transactions[2].refundsTransactionId" },
        { "unknown field", null },
        { "missing field", null },
        { "numeric enum", null },
        { "payment naming half a bank operation", "budgetPayments[0].externalId" },
        { "payment of an unknown bank account", "budgetPayments[0].bankAccountId" },
        { "candidate of an unknown bank account", "budgetPaymentCandidates[0].bankAccountId" },
        { "candidate to a non-Treasury account", "budgetPaymentCandidates[0].counterIban" },
        { "confirmed candidate without a kind", "budgetPaymentCandidates[0].confirmedKind" },
        { "manual treasury account outside the Treasury", "treasuryAccounts[0].manualIban" },
        { "manual treasury account with a 7 digit code", "treasuryAccounts[0].manualIban" },
        { "treasury account end without an account", "treasuryAccounts[1].manualValidUntil" },
        { "treasury account end both set and removed", "treasuryAccounts[0].manualEndRemoved" },
        { "removed treasury account end without an account", "treasuryAccounts[1].manualEndRemoved" },
        { "two treasury accounts of one kind", "treasuryAccounts[1].kind" },
        { "notice without a manual account", "treasuryAccounts[1].noticeAt" },
        { "learned treasury account without its operation", "treasuryAccounts[1].learnedIban" },
        { "a channel address that is not a chat id", "notificationChannels[0].address" },
        { "a reserve jar with a negative balance", "reserveJar.balanceKop" },
        { "a reserve jar without an id", "reserveJar.jarId" },
        { "a reserve jar read in the future", "reserveJar.fetchedAt" },
        { "a reserve jar title with a control character", "reserveJar.title" },
        { "an email address that is not a plain address", "notificationChannels[1].address" },
        { "an email channel enabled before it is confirmed", "notificationChannels[1].enabled" },
        { "a newer schema version", null },
        { "group 3 from the middle of a quarter", "settings.group3Since" },
        { "a blank group 3 receipt number", "settings.confirmation.receiptNumber" },
        { "a group 3 receipt before registration", "settings.confirmation.confirmedOn" },
        { "a group 3 receipt dated after today", "settings.confirmation.confirmedOn" },
        { "country that is not ISO 3166-1", "clients[0].country" },
        { "malformed client email", "clients[0].email" },
        { "no schema version", null },
        { "not JSON", null },
        { "filing for quarter 5", "declarationFilings[0].quarter" },
        { "the same quarter filed twice", "declarationFilings[1].quarter" },
        { "filed before the quarter ended", "declarationFilings[1].filedOn" },
        { "filed after today", "declarationFilings[1].filedOn" },
        { "declaration type out of the enum", null },
        { "a malformed KVED code", "declarationDetails.kvedCodes[1]" },
        { "a district without a region", "declarationDetails.taxOfficeRegion" },
        { "a tax office name with a control character", "declarationDetails.taxOfficeName" },
        { "a null declaration file", "file" },
        { "a declaration file for quarter 5", "declarationFiles[0].quarter" },
        { "a declaration file not named .xml", "declarationFiles[0].fileName" },
        { "an empty declaration file", "declarationFiles[0].content" },
        { "a declaration file over 1 MiB", "declarationFiles[0].content" },
        { "two declaration files of one type", "declarationFiles[1].type" },
        { "a declaration annex without its content", "declarationFiles[0].annexFileName" },
        { "a declaration annex not named .xml", "declarationFiles[0].annexFileName" },
        { "an empty declaration annex", "declarationFiles[0].annexContent" },
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
            Assert.Equal("backup_invalid", problem.RootElement.GetProperty("code").GetString());
            ProblemAssert.Rejects(problem.RootElement, errorKey);
        }

        Assert.Equal(before, await Backup(owner));
    }

    [Fact]
    public async Task Two_restores_at_once_leave_exactly_the_files_rows()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Wipe(owner);
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
            [typeof(BankAccount)] = typeof(BankAccountBackup),
            [typeof(ImportBatch)] = typeof(ImportBatchBackup),
            [typeof(BudgetPaymentCandidate)] = typeof(PaymentCandidateBackup),
            [typeof(InvoicingDetails)] = typeof(InvoicingDetailsBackup),
            [typeof(InvoicingPaymentDetails)] = typeof(PaymentDetailsInput),
            [typeof(Invoice)] = typeof(InvoiceBackup),
            [typeof(DeclarationDetails)] = typeof(DeclarationDetailsBackup),
            [typeof(DeclarationFiling)] = typeof(DeclarationFilingBackup),
            [typeof(DeclarationFile)] = typeof(DeclarationFileBackup),
            [typeof(TreasuryAccount)] = typeof(TreasuryAccountBackup),
            [typeof(NotificationChannel)] = typeof(NotificationChannelBackup),
            [typeof(ReserveJar)] = typeof(ReserveJarBackup),
        };
        Type[] sharedByEveryOwner = [typeof(TaxYearConfig), typeof(LimitationSuspensionConfig), typeof(FxRate)];
        // The change log is history, not state: a restore does not replay it and does not carry it.
        Type[] historyNotState = [typeof(AuditEntry)];
        // ADR-011: the encrypted token must never leave the database, so the connection it belongs to is
        // excluded outright rather than carried with the token blanked. A restore leaves it absent and the
        // owner reconnects; bank accounts are carried, so reconnecting finds the rows imports point at.
        Type[] bankConnectionNotBackedUp = [typeof(MonobankConnection)];
        // The foreign legs of currency sales are the bank's record, read again by the walk a restore
        // starts; like the cursors below, they describe the bank, not the owner's ledger (Rule 12).
        Type[] bankRecordNotBackedUp = [typeof(ForeignDebit)];
        // A link code is a secret of the running server and the poll offset belongs to the bot, not to
        // the owner; neither is written to a file.
        // The sent-reminder log is what already reached the owner's chat, not the owner's data; a restore
        // leaves it in place so restoring does not send the same reminder twice.
        Type[] telegramRuntimeNotBackedUp = [typeof(NotificationLinkCode), typeof(TelegramPollState), typeof(SentReminder)];
        // The feed secret is a bearer credential for the owner's deadlines (ADR-017). A restore creates
        // none and leaves the current one alone; the owner rotates to get one.
        Type[] calendarSecretNotBackedUp = [typeof(CalendarFeed)];
        // The backup sidecar's log of its own runs is the whole database's, with no owner in it.
        Type[] databaseOperationsNotBackedUp = [typeof(DatabaseBackupRun)];

        var featureTables = model.GetEntityTypes()
            .Select(type => type.ClrType)
            .Where(type => type.Namespace!.StartsWith("TaxesUa.Api.Features.", StringComparison.Ordinal)
                && type.Namespace != "TaxesUa.Api.Features.Auth")
            .ToHashSet();
        Assert.Equal(
            backedUp.Keys
                .Concat(sharedByEveryOwner)
                .Concat(historyNotState)
                .Concat(bankConnectionNotBackedUp)
                .Concat(bankRecordNotBackedUp)
                .Concat(telegramRuntimeNotBackedUp)
                .Concat(calendarSecretNotBackedUp)
                .Concat(databaseOperationsNotBackedUp)
                .ToHashSet(),
            featureTables);

        // A sync's cursor, history mark and last failure describe the database's imports, which a restore
        // replaces, so a restore clears them rather than carrying them (Rule 12).
        string[] syncStateNotBackedUp =
        [
            nameof(BankAccount.SyncedThrough),
            nameof(BankAccount.HistoryImportedAt),
            nameof(BankAccount.BackfillStartedAt),
            nameof(BankAccount.LastFailedAt),
            nameof(BankAccount.LastFailure),
        ];

        string[] channelBookkeeping =
        [
            nameof(NotificationChannel.Id),
            nameof(NotificationChannel.LastDeliveryAt),
            nameof(NotificationChannel.LastFailure),
            nameof(NotificationChannel.LastFailureAt),
        ];

        // When the language and theme were chosen only orders choices between the owner's devices (ADR-032).
        // A restore leaves them unknown, the oldest, so each device's own later choice wins over the file's.
        string[] appearanceTimesNotBackedUp = [nameof(SettingsEntity.LocaleChosenAt), nameof(SettingsEntity.ThemeChosenAt)];

        foreach (var (entity, record) in backedUp)
        {
            var columns = model.FindEntityType(entity)!.GetProperties().Select(property => property.Name)
                .Where(name => name != "UserId")
                // A restore draws fresh ids for the payment details, which nothing else refers to.
                .Where(name => entity != typeof(InvoicingPaymentDetails) || name != nameof(InvoicingPaymentDetails.Id))
                // An account is identified by its owner and kind; the row's own id is drawn again on restore.
                .Where(name => entity != typeof(TreasuryAccount) || name != nameof(TreasuryAccount.Id))
                // A channel is identified by its owner and kind; its delivery record starts clean on restore.
                .Where(name => entity != typeof(NotificationChannel) || !channelBookkeeping.Contains(name))
                // The total is the sum of the lines, recomputed on restore rather than trusted from the file.
                .Where(name => entity != typeof(Invoice) || name != nameof(Invoice.TotalMinor))
                .Where(name => entity != typeof(BankAccount) || !syncStateNotBackedUp.Contains(name))
                .Where(name => entity != typeof(SettingsEntity) || !appearanceTimesNotBackedUp.Contains(name))
                // The year and the quarter of the return to group 3 travel as one YearQuarter, and the group 3
                // receipt's date and number as one confirmation.
                .Select(name => entity == typeof(SettingsEntity) && name.StartsWith(nameof(SettingsEntity.BackOnGroup3From), StringComparison.Ordinal)
                    ? nameof(SettingsEntity.BackOnGroup3From)
                    : entity == typeof(SettingsEntity) && name is nameof(SettingsEntity.Group3ConfirmedOn) or nameof(SettingsEntity.Group3ReceiptNumber)
                        ? nameof(SettingsBackup.Group3Confirmation)
                        : name);
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

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(
            "/api/settings/declaration",
            new DeclarationDetailsRequest(26, 5, "ГУ ДПС у м. Києві", ["62.01"], "Київ, вул. Тестова 1"),
            Json)).StatusCode);

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

        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync(
                "/api/settings/treasury-accounts/MilitaryLevy",
                new TreasuryAccountRequest(TreasuryIban, "ГУК у м.Києві", "37993783"),
                Json)).StatusCode);
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
                true, [DayOfWeek.Saturday, DayOfWeek.Sunday], "uk", "system", "UAH", new YearQuarter(2032, 2),
                null, new Group3ConfirmationBackup(new DateOnly(2031, 1, 5), "9123456789"), true, true, false),
            [
                new ClientBackup(
                    ClientId, "Acme", "1 Main St, Berlin", "DE", "DE123456789", "ap@acme.example", Currency.EUR, "Net 14"),
                new ClientBackup(Guid.NewGuid(), "Beta", null, null, null, null, null, null),
            ],
            [
                new TransactionBackup(UahReceiptId, new DateOnly(2031, 2, 1), 100_000, Currency.UAH, Money.RateScale,
                    null, null, 100_000, TransactionKind.Income, null, ClientId, null, null, null, null, null, null, null, null, null, ReviewStatus.Confirmed, created, created),
                new TransactionBackup(UsdReceiptId, new DateOnly(2031, 2, 2), 100_000, Currency.USD, 400_000,
                    null, RateSource.Manual, 4_000_000, TransactionKind.Income, null, null, null, null, "INV-2", null, null, null, null, null, null,
                    ReviewStatus.Confirmed, created, created),
                new TransactionBackup(UsdRefundId, new DateOnly(2031, 2, 3), 30_000, Currency.USD, 400_000,
                    null, RateSource.Manual, 1_200_000, TransactionKind.RefundToClient, null, null, UsdReceiptId, null, null,
                    null, null, null, null, null, null, ReviewStatus.Confirmed, created, created),
                new TransactionBackup(TransferId, new DateOnly(2031, 2, 4), 5_000, Currency.UAH, Money.RateScale,
                    null, null, 5_000, TransactionKind.OwnTransfer, "Own card", null, null, null, null, null, null, null, null, null, null,
                    ReviewStatus.Confirmed, created, created),
            ],
            [
                new BudgetPaymentBackup(QuarterPaymentId, new DateOnly(2031, 4, 15), PaymentKind.Esv, 190_234, 2031, 1,
                    null, null, null, null, created, created),
                new BudgetPaymentBackup(MonthPaymentId, new DateOnly(2031, 3, 15), PaymentKind.SingleTax, 61_700, 2031,
                    null, 3, "March", null, null, created, created),
            ],
            [],
            [],
            [],
            new InvoicingDetailsBackup(
                "ФОП Тест Тестович",
                "FOP Test Testovych",
                "1234567890",
                "Київ, вул. Тестова 1",
                "1 Testova St, Kyiv",
                InvoicingDefaults.AcceptanceEn,
                InvoicingDefaults.AcceptanceUk,
                InvoicingDefaults.FeesEn,
                "Комісії сплачує платник.",
                InvoicingDefaults.TaxStatusEn,
                InvoicingDefaults.TaxStatusUk,
                [
                    new PaymentDetailsInput(
                        Currency.USD, InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "Intermediary Bank", "IRVTUS3N", "0011223344"),
                    new PaymentDetailsInput(Currency.EUR, InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "", "", ""),
                ],
                Convert.ToBase64String(InvoicingTestData.Png),
                "image/png",
                created),
            [
                new InvoiceBackup(
                    IssuedInvoiceId, ClientId, InvoiceStatus.Issued, 2031, 1, new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 15),
                    Currency.EUR,
                    [new InvoiceLine("Software development", "Розробка програмного забезпечення", InvoiceUnit.Hour, 12_500, 40_00)],
                    new InvoiceSnapshot(
                        new InvoiceSeller("ФОП Тест Тестович", "FOP Test Testovych", "1234567890", "Київ, вул. Тестова 1", "1 Testova St, Kyiv"),
                        new InvoiceBuyer("Acme", "1 Main St, Berlin", "DE", "Germany", "DE123456789", "ap@acme.example"),
                        new InvoicePayment(InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "", "", ""),
                        new InvoiceClauses(
                            InvoicingDefaults.AcceptanceEn, InvoicingDefaults.AcceptanceUk, InvoicingDefaults.FeesEn,
                            InvoicingDefaults.FeesUk, InvoicingDefaults.TaxStatusEn, InvoicingDefaults.TaxStatusUk)),
                    Convert.ToBase64String(InvoicingTestData.Png),
                    "image/png",
                    null,
                    created,
                    null,
                    created,
                    created),
                new InvoiceBackup(
                    DraftInvoiceId, ClientId, InvoiceStatus.Draft, null, null, new DateOnly(2031, 6, 1), new DateOnly(2031, 6, 15),
                    Currency.EUR,
                    [new InvoiceLine("Support", "Підтримка", InvoiceUnit.Month, 1_000, 500_00)],
                    null, null, null, null, null, null, created.AddDays(1), created.AddDays(1)),
            ],
            new DeclarationDetailsBackup(
                26, 5, "ГУ ДПС у м. Києві", ["62.01", "63.11"], "Київ, вул. Тестова 1", "Тестенко Тест Тестович", "+380501234567", "fop@example.com"),
            [
                new DeclarationFilingBackup(2030, 4, new DateOnly(2031, 2, 3), DeclarationType.Reporting, 90_000_000, created, created),
                new DeclarationFilingBackup(2031, 1, new DateOnly(2031, 5, 5), DeclarationType.Clarifying, 4_900_000, created, created),
            ],
            [
                new DeclarationFileBackup(
                    2031, 1, DeclarationType.Reporting, "26051234567890F0103309100000000120320312605.xml", DeclarationFileBytes,
                    "26051234567890F0133109100000000120320312605.xml", AnnexFileBytes, created),
                new DeclarationFileBackup(
                    2031, 1, DeclarationType.Clarifying, "26051234567890F0103309300000000120320312605.xml", [0x3C, 0x00, 0xFF],
                    null, null, created.AddDays(4)),
            ],
            [
                new TreasuryAccountBackup(
                    PaymentKind.SingleTax, TreasuryIban, "ГУК у м.Києві", "37993783", created, new DateOnly(2031, 12, 31), false,
                    LearnedTreasuryIban, "ГУК у м.Києві/Печерс.р-н", "37993784", "op-learned", new DateOnly(2031, 4, 15), created,
                    new DateOnly(2032, 6, 30), false, created),
                new TreasuryAccountBackup(
                    PaymentKind.Esv, null, null, null, null, null, false,
                    LearnedTreasuryIban, null, null, "op-learned-esv", new DateOnly(2031, 4, 16), created, null, false, null),
            ],
            [new NotificationChannelBackup(NotificationChannelKind.Telegram, "424242", true, created, created)],
            new ReserveJarBackup("jar-taxes", "На податки", 12_345_00, created.AddHours(3)));

        return JsonSerializer.SerializeToNode(document, Json)!.AsObject();
    }

    private static void AddEmailChannel(JsonObject file, string address, bool enabled, string? confirmedAt) =>
        file["notificationChannels"]!.AsArray().Add(new JsonObject
        {
            ["kind"] = "Email",
            ["address"] = address,
            ["enabled"] = enabled,
            ["linkedAt"] = "2031-03-01T08:00:00+00:00",
            ["confirmedAt"] = confirmedAt,
        });

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
            case "invoicing details with a bad IBAN":
                file["invoicingDetails"]!["paymentDetails"]![0]!["iban"] = "UA00";
                break;
            case "signature that is not base64":
                file["invoicingDetails"]!["signatureImage"] = "***";
                break;
            case "signature that is not the declared type":
                file["invoicingDetails"]!["signatureContentType"] = "image/jpeg";
                break;
            case "signature without a type":
                file["invoicingDetails"]!["signatureContentType"] = null;
                break;
            case "invoice of an unknown client":
                file["invoices"]![0]!["clientId"] = Guid.NewGuid();
                break;
            case "invoice numbered in another year":
                file["invoices"]![0]!["numberYear"] = 2030;
                break;
            case "draft with a number":
                file["invoices"]![1]!["numberYear"] = 2031;
                file["invoices"]![1]!["numberSequence"] = 2;
                break;
            case "repeated invoice number":
                file["invoices"]![1]!["status"] = "Issued";
                file["invoices"]![1]!["numberYear"] = 2031;
                file["invoices"]![1]!["numberSequence"] = 1;
                file["invoices"]![1]!["snapshot"] = file["invoices"]![0]!["snapshot"]!.DeepClone();
                file["invoices"]![1]!["issuedAt"] = file["invoices"]![0]!["issuedAt"]!.DeepClone();
                break;
            case "invoice line with no quantity":
                file["invoices"]![0]!["lines"]![0]!["quantityThousandths"] = 0;
                break;
            case "signature over the size cap":
                file["invoicingDetails"]!["signatureImage"] =
                    Convert.ToBase64String(new byte[(512 * 1024) + 1].Select((_, i) => i < 8 ? InvoicingTestData.Png[i] : (byte)0).ToArray());
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
            case "comma-joined transaction kind":
                transactions[0]!["kind"] = "Income, RefundToClient";
                break;
            case "NUL in a client name":
                file["clients"]![0]!["name"] = "Ann\u0000a";
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
            case "future-dated transaction":
                // Today is 2031-06-01 for this test class (see CreateApplication), so this is a day after.
                transactions[0]!["valueDate"] = "2031-06-02";
                break;
            case "dismissed typed row":
                transactions[0]!["reviewStatus"] = "Dismissed";
                break;
            case "dismissed import keeping its refund link":
                Dismiss(file, transactions[2]!);
                break;
            case "refund of a dismissed import":
                Dismiss(file, transactions[1]!);
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
            case "payment naming half a bank operation":
                payments[0]!["externalId"] = "op-half";
                break;
            case "payment of an unknown bank account":
                payments[0]!["bankAccountId"] = Guid.NewGuid();
                payments[0]!["externalId"] = "op-unknown";
                break;
            case "candidate of an unknown bank account":
                AddCandidate(file, Guid.NewGuid(), TreasuryIban, "Pending", null);
                break;
            case "candidate to a non-Treasury account":
                AddCandidate(file, AddAccount(file), "UA753220010000026001234567891", "Pending", null);
                break;
            case "confirmed candidate without a kind":
                AddCandidate(file, AddAccount(file), TreasuryIban, "Confirmed", null);
                break;
            case "manual treasury account outside the Treasury":
                file["treasuryAccounts"]![0]!["manualIban"] = "UA753220010000026001234567891";
                break;
            case "manual treasury account with a 7 digit code":
                file["treasuryAccounts"]![0]!["manualRecipientCode"] = "3799378";
                break;
            case "treasury account end without an account":
                file["treasuryAccounts"]![1]!["manualValidUntil"] = "2031-12-31";
                break;
            case "treasury account end both set and removed":
                file["treasuryAccounts"]![0]!["manualValidUntil"] = "2031-12-31";
                file["treasuryAccounts"]![0]!["manualEndRemoved"] = true;
                break;
            case "removed treasury account end without an account":
                file["treasuryAccounts"]![1]!["manualEndRemoved"] = true;
                break;
            case "two treasury accounts of one kind":
                file["treasuryAccounts"]![1]!["kind"] = "SingleTax";
                break;
            case "notice without a manual account":
                file["treasuryAccounts"]![1]!["noticeAt"] = "2031-04-16T09:00:00+00:00";
                break;
            case "learned treasury account without its operation":
                file["treasuryAccounts"]![1]!["learnedExternalId"] = null;
                break;
            case "a channel address that is not a chat id":
                file["notificationChannels"]![0]!["address"] = "someone@example.com";
                break;
            case "a reserve jar with a negative balance":
                file["reserveJar"]!["balanceKop"] = -1;
                break;
            case "a reserve jar read in the future":
                file["reserveJar"]!["fetchedAt"] = "2031-06-01T10:06:00+00:00";
                break;
            case "a reserve jar without an id":
                file["reserveJar"]!["jarId"] = string.Empty;
                break;
            case "a reserve jar title with a control character":
                file["reserveJar"]!["title"] = "jar\u0007";
                break;
            case "an email address that is not a plain address":
                AddEmailChannel(file, "Owner <owner@mail.test>", true, "2031-03-01T08:05:00+00:00");
                break;
            case "an email channel enabled before it is confirmed":
                AddEmailChannel(file, "owner@mail.test", true, null);
                break;
            case "a newer schema version":
                file["schemaVersion"] = BackupDocument.CurrentSchemaVersion + 1;
                break;
            case "group 3 from the middle of a quarter":
                file["settings"]!["group3Since"] = "2031-02-01";
                break;
            case "a blank group 3 receipt number":
                file["settings"]!["group3Confirmation"]!["receiptNumber"] = "  ";
                break;
            case "a group 3 receipt before registration":
                file["settings"]!["group3Confirmation"]!["confirmedOn"] = "2030-12-31";
                break;
            case "a group 3 receipt dated after today":
                file["settings"]!["group3Confirmation"]!["confirmedOn"] = "2031-06-02";
                break;
            case "country that is not ISO 3166-1":
                file["clients"]![0]!["country"] = "XX";
                break;
            case "malformed client email":
                file["clients"]![0]!["email"] = "not an email";
                break;
            case "no schema version":
                file.Remove("schemaVersion");
                break;
            case "filing for quarter 5":
                file["declarationFilings"]![0]!["quarter"] = 5;
                break;
            case "the same quarter filed twice":
                file["declarationFilings"]![1]!["year"] = 2030;
                file["declarationFilings"]![1]!["quarter"] = 4;
                break;
            case "filed before the quarter ended":
                file["declarationFilings"]![1]!["filedOn"] = "2031-03-31";
                break;
            case "filed after today":
                file["declarationFilings"]![1]!["filedOn"] = "2031-06-02";
                break;
            case "declaration type out of the enum":
                file["declarationFilings"]![0]!["type"] = "Final";
                break;
            case "a malformed KVED code":
                file["declarationDetails"]!["kvedCodes"] = new JsonArray("62.01", "6201");
                break;
            case "a district without a region":
                file["declarationDetails"]!["taxOfficeRegion"] = null;
                break;
            case "a tax office name with a control character":
                file["declarationDetails"]!["taxOfficeName"] = "ГУ ДПС\u0007";
                break;
            case "a null declaration file":
                file["declarationFiles"]!.AsArray().Add(null);
                break;
            case "a declaration file for quarter 5":
                file["declarationFiles"]![0]!["quarter"] = 5;
                break;
            case "a declaration file not named .xml":
                file["declarationFiles"]![0]!["fileName"] = "declaration.pdf";
                break;
            case "an empty declaration file":
                file["declarationFiles"]![0]!["content"] = string.Empty;
                break;
            case "a declaration file over 1 MiB":
                file["declarationFiles"]![0]!["content"] = Convert.ToBase64String(new byte[1024 * 1024 + 1]);
                break;
            case "a declaration annex without its content":
                file["declarationFiles"]![0]!["annexContent"] = null;
                break;
            case "a declaration annex not named .xml":
                file["declarationFiles"]![0]!["annexFileName"] = "annex.pdf";
                break;
            case "an empty declaration annex":
                file["declarationFiles"]![0]!["annexContent"] = string.Empty;
                break;
            case "two declaration files of one type":
                file["declarationFiles"]![1]!["type"] = "Reporting";
                break;
            case "not JSON":
                return "{\"schemaVersion\":1,";
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }

        return file.ToJsonString();
    }

    private const string TreasuryIban = "UA358999980333159998000026011";

    // Not XML at all: a restore carries the stored bytes without reading them, and 0xCF 0xB2 are windows-1251.
    private static readonly byte[] DeclarationFileBytes = [0x3C, 0xCF, 0xB2, 0x3E];

    private static readonly byte[] AnnexFileBytes = [0x3C, 0xC4, 0x31, 0x3E];

    private const string LearnedTreasuryIban = "UA148999980313181000026007233";

    private static Guid AddAccount(JsonObject file)
    {
        var account = Guid.NewGuid();
        file["bankAccounts"]!.AsArray().Add(JsonSerializer.SerializeToNode(
            new BankAccountBackup(account, Bank.Monobank, "uah", "UAH", 980, "", "fop", true, true, DateTimeOffset.UnixEpoch),
            Json));
        return account;
    }

    private static void AddCandidate(JsonObject file, Guid account, string iban, string status, string? confirmedKind) =>
        file["budgetPaymentCandidates"]!.AsArray().Add(new JsonObject
        {
            ["id"] = Guid.NewGuid(),
            ["bankAccountId"] = account,
            ["externalId"] = "op-candidate",
            ["bankTime"] = "2031-04-15T09:00:00+00:00",
            ["amountKop"] = 190_234,
            ["counterIban"] = iban,
            ["counterName"] = "ГУК у м.Києві",
            ["counterEdrpou"] = "37993783",
            ["purpose"] = "ЄСВ",
            ["status"] = status,
            ["confirmedKind"] = confirmedKind,
            ["createdAt"] = "2031-04-15T09:00:00+00:00",
            ["resolvedAt"] = null,
        });

    private static void Dismiss(JsonObject file, JsonNode transaction)
    {
        var account = Guid.NewGuid();
        file["bankAccounts"]!.AsArray().Add(JsonSerializer.SerializeToNode(
            new BankAccountBackup(account, Bank.Monobank, "usd", "USD", 840, "", "fop", true, true, DateTimeOffset.UnixEpoch),
            Json));
        transaction["bankAccountId"] = account;
        transaction["externalId"] = "op-dismissed";
        transaction["reviewStatus"] = "Dismissed";
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string file) =>
        client.PostAsync("/api/restore", new StringContent(file, Encoding.UTF8, "application/json"));

    // A restore never drops an issued invoice, so a test that needs one gone deletes it directly.
    private async Task DropInvoices()
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices.ExecuteDeleteAsync();
    }

    private async Task Wipe(HttpClient owner)
    {
        await DropInvoices();
        await Restore(owner, Empty);
    }

    private static async Task<RestoreResponse> Restore(HttpClient owner, string file)
    {
        var response = await Post(owner, file);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RestoreResponse>(Json))!;
    }

    private static async Task<AuditEntryResponse[]> History(HttpClient owner, AuditedEntity? entity, string? id)
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

        var response = await owner.GetAsync($"/api/audit?{string.Join('&', query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuditEntryResponse[]>(Json))!;
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
