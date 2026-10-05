using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Tests.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Backup;

// A backup is a file the owner keeps for years, so a build must restore the file of every schema it ever wrote,
// not only what its own exporter writes today. Fixtures/backup-vNN.json is one small owner's file as schema
// NN wrote it: v19 is the real exporter's output for that owner, and each older file is that file cut down to
// the shape the schema's changelog (the comment on BackupDocument.CurrentSchemaVersion) says it had, with the
// default the owner then lived with where a field was added later (Prorated before version 16).
// Adding a version means adding its fixture; the first test fails until it exists.
public sealed class BackupSchemaVersionsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // The oldest file with a fixture. Version 1 has no bank import at all and is covered by BackupEndpointsTests.
    private const int FirstVersion = 2;

    private static readonly DateOnly Today = new(2026, 10, 15);

    public static TheoryData<int> Versions
    {
        get
        {
            var versions = new TheoryData<int>();
            for (var version = FirstVersion; version <= BackupDocument.CurrentSchemaVersion; version++)
            {
                versions.Add(version);
            }

            return versions;
        }
    }

    [Fact]
    public void Every_schema_version_has_a_fixture_that_says_which_version_it_is()
    {
        for (var version = FirstVersion; version <= BackupDocument.CurrentSchemaVersion; version++)
        {
            Assert.Equal(version, Fixture(version)["schemaVersion"]!.GetValue<int>());
        }
    }

    // The current fixture is the exporter's own output, so an exporter change without a version bump shows here.
    [Fact]
    public async Task The_current_fixture_restores_and_exports_back_unchanged()
    {
        await using var application = fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>()), Today);
        using var owner = await ApiFixture.SignIn(application, fixture.NewOwner());
        var file = Fixture(BackupDocument.CurrentSchemaVersion);

        var response = await owner.PostAsync(
            "/api/restore", new StringContent(file.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var backup = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        Assert.Equal(WithIdsByPosition(file), WithIdsByPosition(backup));
    }

    // A restore takes fresh ids when another owner already holds the file's, so ids compare by where they first
    // appear and the links between rows still have to match.
    private static string WithIdsByPosition(JsonNode file)
    {
        var seen = new Dictionary<string, int>();
        return System.Text.RegularExpressions.Regex.Replace(
            file.ToJsonString(),
            "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
            match =>
            {
                seen.TryAdd(match.Value, seen.Count);
                return $"id-{seen[match.Value]}";
            });
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_file_of_each_schema_version_restores_with_its_data_and_the_defaults_of_what_it_predates(int version)
    {
        await using var application = fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>()), Today);
        using var owner = await ApiFixture.SignIn(application, fixture.NewOwner());
        var file = Fixture(version);

        var response = await owner.PostAsync(
            "/api/restore", new StringContent(file.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(new RestoreResponse(2, 4, 2), await response.Content.ReadFromJsonAsync<RestoreResponse>());

        var backup = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!.AsObject();
        Assert.Equal(BackupDocument.CurrentSchemaVersion, backup["schemaVersion"]!.GetValue<int>());

        // What every version had.
        var settings = backup["settings"]!;
        Assert.Equal("2026-01-12", settings["fopRegistrationDate"]!.GetValue<string>());
        Assert.Equal("Quarterly", settings["paymentMode"]!.GetValue<string>());
        Assert.Equal("FullMonth", settings["esvRegistrationMonthPolicy"]!.GetValue<string>());
        Assert.Equal(["Acme GmbH", "Бета ТОВ"], Names(backup, "clients", "name"));
        Assert.Equal(
            [("UAH", 25_000_000L, 25_000_000L), ("USD", 300_000L, 12_370_350L), ("UAH", 500_000L, 500_000L), ("EUR", 50_000L, 2_250_000L)],
            backup["transactions"]!.AsArray().Select(row =>
                (row!["currency"]!.GetValue<string>(), row["amountMinor"]!.GetValue<long>(), row["amountUahKop"]!.GetValue<long>())).ToArray());
        Assert.Equal(
            [("Esv", 570_702L), ("SingleTax", 1_868_518L)],
            backup["budgetPayments"]!.AsArray().Select(row => (row!["kind"]!.GetValue<string>(), row["amountKop"]!.GetValue<long>())).ToArray());

        // The API reads what was restored: the owner's year in the ledger is the three receipts that count.
        var transactions = (await owner.GetFromJsonAsync<JsonObject>("/api/transactions?year=2026"))!;
        Assert.Equal(39_620_350, transactions["totalIncomeKop"]!.GetValue<long>());

        // Version 2 added the bank accounts, the import batches and the transactions' import fields.
        Assert.Equal("UA213223130000026007233566001", Assert.Single(backup["bankAccounts"]!.AsArray())!["iban"]!.GetValue<string>());
        Assert.Single(backup["importBatches"]!.AsArray());
        Assert.Equal("mono-op-1", backup["transactions"]![0]!["externalId"]!.GetValue<string>());

        Version3(version, backup);
        Version4(version, backup);
        Version5(version, backup);
        Version6(version, backup);
        Version7(version, backup);
        Version8(version, backup);
        Version9(version, backup);
        Version10(version, backup);
        Version11(version, backup);
        Version12(version, backup);
        Version13(version, backup);
        Version14(version, backup);
        Version16(version, backup);
        Version17(version, backup);
        Version18(version, backup);
        Version19(version, backup);
    }

    // The payments' bank operation and the payment candidates.
    private static void Version3(int version, JsonObject backup)
    {
        var candidates = backup["budgetPaymentCandidates"]!.AsArray();
        var bankOperation = backup["budgetPayments"]![0]!["externalId"]?.GetValue<string>();
        if (version < 3)
        {
            Assert.Empty(candidates);
            Assert.Null(bankOperation);
            return;
        }

        Assert.Equal("mono-op-esv", bankOperation);
        Assert.Equal(
            [("mono-op-esv", "Confirmed", "Esv"), ("mono-op-tax", "Pending", null)],
            candidates.Select(row => (row!["externalId"]!.GetValue<string>(), row["status"]!.GetValue<string>(), row["confirmedKind"]?.GetValue<string>())).ToArray());
    }

    // The invoicing details, with the signature image.
    private static void Version4(int version, JsonObject backup)
    {
        var details = backup["invoicingDetails"];
        if (version < 4)
        {
            Assert.Null(details);
            return;
        }

        Assert.Equal("FOP Test Testovych", details!["sellerNameEn"]!.GetValue<string>());
        Assert.Equal("image/png", details["signatureContentType"]!.GetValue<string>());
        Assert.Equal("EUR", Assert.Single(details["paymentDetails"]!.AsArray())!["currency"]!.GetValue<string>());
    }

    // The clients' details.
    private static void Version5(int version, JsonObject backup)
    {
        var acme = backup["clients"]![0]!;
        Assert.Equal(
            version < 5 ? (null, null, null) : ("DE123456789", "ap@acme.example", "EUR"),
            (acme["vatId"]?.GetValue<string>(), acme["email"]?.GetValue<string>(), acme["defaultCurrency"]?.GetValue<string>()));
    }

    // The invoices.
    private static void Version6(int version, JsonObject backup)
    {
        var invoices = backup["invoices"]!.AsArray();
        if (version < 6)
        {
            Assert.Empty(invoices);
            return;
        }

        Assert.Equal(
            [("Issued", 1), ("Draft", 0)],
            invoices.Select(row => (row!["status"]!.GetValue<string>(), row["numberSequence"]?.GetValue<int>() ?? 0)).ToArray());
        var line = Assert.Single(invoices[0]!["lines"]!.AsArray())!;
        Assert.Equal((12_500, 4_000), (line["quantityThousandths"]!.GetValue<int>(), line["rateMinor"]!.GetValue<int>()));
    }

    // The declaration details and the filed marks.
    private static void Version7(int version, JsonObject backup)
    {
        if (version < 7)
        {
            Assert.Null(backup["declarationDetails"]);
            Assert.Empty(backup["declarationFilings"]!.AsArray());
            return;
        }

        Assert.Equal(["62.01"], backup["declarationDetails"]!["kvedCodes"]!.AsArray().Select(code => code!.GetValue<string>()).ToArray());
        var filing = Assert.Single(backup["declarationFilings"]!.AsArray())!;
        Assert.Equal((2026, 1, "2026-05-05", 37_370_350L), (
            filing["year"]!.GetValue<int>(), filing["quarter"]!.GetValue<int>(),
            filing["filedOn"]!.GetValue<string>(), filing["filedIncomeKop"]!.GetValue<long>()));
    }

    // The receipt paying an invoice.
    private static void Version8(int version, JsonObject backup)
    {
        var receipt = backup["transactions"]![3]!;
        if (version < 8)
        {
            Assert.Null(receipt["invoiceId"]);
            return;
        }

        Assert.Equal(backup["invoices"]![0]!["id"]!.GetValue<Guid>(), receipt["invoiceId"]!.GetValue<Guid>());
    }

    // The Treasury accounts and the candidates' counterparty code.
    private static void Version9(int version, JsonObject backup)
    {
        var accounts = backup["treasuryAccounts"]!.AsArray();
        var code = version < 3 ? null : backup["budgetPaymentCandidates"]![0]!["counterEdrpou"]?.GetValue<string>();
        if (version < 9)
        {
            Assert.Empty(accounts);
            Assert.Null(code);
            return;
        }

        var account = accounts[0]!;
        Assert.Equal(version < 19 ? 1 : 2, accounts.Count);
        Assert.Equal(("SingleTax", "UA358999980333159998000026011", "UA148999980313181000026007233"), (
            account["kind"]!.GetValue<string>(), account["manualIban"]!.GetValue<string>(), account["learnedIban"]!.GetValue<string>()));
        Assert.Equal("37993783", code);
    }

    // The return to group 3 after a limit crossing.
    private static void Version10(int version, JsonObject backup)
    {
        var back = backup["settings"]!["backOnGroup3From"];
        if (version < 10)
        {
            Assert.Null(back);
            return;
        }

        Assert.Equal((2027, 1), (back!["year"]!.GetValue<int>(), back["quarter"]!.GetValue<int>()));
    }

    // The notification channels, and from version 15 the time each was confirmed (a Telegram chat is confirmed
    // when it is linked, so a file from before that gets the link time). A restore brings every channel back
    // unconfirmed and off (#180), so the owner's export after it holds no confirmation: the v17 and later fixtures are that
    // export, and it is why its Telegram channel is off.
    private static void Version11(int version, JsonObject backup)
    {
        var channels = backup["notificationChannels"]!.AsArray();
        if (version < 11)
        {
            Assert.Empty(channels);
            return;
        }

        var channel = Assert.Single(channels)!;
        Assert.Equal(("Telegram", "424242", false), (
            channel["kind"]!.GetValue<string>(), channel["address"]!.GetValue<string>(), channel["enabled"]!.GetValue<bool>()));
        Assert.Null(channel["confirmedAt"]);
    }

    // The declaration file and the tax office's name.
    private static void Version12(int version, JsonObject backup)
    {
        var files = backup["declarationFiles"]!.AsArray();
        var office = backup["declarationDetails"]?["taxOfficeName"]?.GetValue<string>();
        if (version < 12)
        {
            Assert.Empty(files);
            Assert.Equal(version < 7 ? null : string.Empty, office);
            return;
        }

        Assert.Equal("ГУ ДПС у м. Києві", office);
        var file = Assert.Single(files)!;
        Assert.Equal("26051234567890F0103309100000000120320262605.xml", file["fileName"]!.GetValue<string>());
        Assert.Equal(new byte[] { 0x3C, 0xCF, 0xB2, 0x3E }, Convert.FromBase64String(file["content"]!.GetValue<string>()));
    }

    // The ESV annex.
    private static void Version13(int version, JsonObject backup)
    {
        if (version < 12)
        {
            return;
        }

        var file = Assert.Single(backup["declarationFiles"]!.AsArray())!;
        Assert.Equal(
            version < 13 ? (null, null) : ("26051234567890F0133109100000000120320262605.xml", Convert.ToBase64String([0x3C, 0xC4, 0x31, 0x3E])),
            (file["annexFileName"]?.GetValue<string>(), file["annexContent"]?.GetValue<string>()));
    }

    // The reserve jar.
    private static void Version14(int version, JsonObject backup)
    {
        var jar = backup["reserveJar"];
        if (version < 14)
        {
            Assert.Null(jar);
            return;
        }

        Assert.Equal(("jar-taxes", 1_234_500L), (jar!["jarId"]!.GetValue<string>(), jar["balanceKop"]!.GetValue<long>()));
    }

    // The DPS status. The Prorated a file wrote before version 16 was the old default, read as FullMonth (checked
    // above for every version); the status itself starts unset.
    private static void Version16(int version, JsonObject backup)
    {
        var settings = backup["settings"]!;
        var status = (
            settings["group3Since"]?.GetValue<string>(),
            settings["group3Confirmation"]?["receiptNumber"]?.GetValue<string>(),
            settings["dpsFopRegistered"]!.GetValue<bool>(),
            settings["dpsEsvRegistered"]!.GetValue<bool>(),
            settings["dpsAccountsRegistered"]!.GetValue<bool>());
        Assert.Equal(
            version < 16 ? ((string?)null, (string?)null, false, false, false) : (null, "9123456789", true, true, false),
            status);
    }

    // The end of a Treasury account's validity.
    private static void Version17(int version, JsonObject backup)
    {
        if (version < 9)
        {
            return;
        }

        var account = backup["treasuryAccounts"]![0]!;
        Assert.Equal(
            version < 17 ? ((string?)null, (string?)null) : ("2026-12-31", "2027-06-30"),
            (account["manualValidUntil"]?.GetValue<string>(), account["learnedValidUntil"]?.GetValue<string>()));
    }

    // The full name, phone and email the declaration's header prints; an older file restores them empty.
    private static void Version18(int version, JsonObject backup)
    {
        if (version < 7)
        {
            return;
        }

        var details = backup["declarationDetails"]!;
        Assert.Equal(
            version < 18 ? ("", "", "") : ("Тестенко Тест Тестович", "+380501234567", "fop@example.com"),
            (details["fullName"]!.GetValue<string>(), details["phone"]!.GetValue<string>(), details["reportEmail"]!.GetValue<string>()));
    }

    // An end the owner removed, which also sets the tax year's default aside; before it no end was removed.
    private static void Version19(int version, JsonObject backup)
    {
        if (version < 9)
        {
            return;
        }

        Assert.Equal(
            version < 19 ? [(false, false)] : [(false, false), (true, false)],
            backup["treasuryAccounts"]!.AsArray().Select(account => (
                account!["manualEndRemoved"]!.GetValue<bool>(), account["learnedEndRemoved"]!.GetValue<bool>())));
    }

    private static string[] Names(JsonObject backup, string array, string field) =>
        [.. backup[array]!.AsArray().Select(row => row![field]!.GetValue<string>())];

    private static JsonObject Fixture(int version) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BackupFixtures", $"backup-v{version:00}.json")))!.AsObject();
}
