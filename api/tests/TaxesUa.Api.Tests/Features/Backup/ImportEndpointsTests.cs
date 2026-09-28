using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Tests.Features.Backup;

// Every test starts by restoring an empty backup, so it owns the whole state of the owner it signs in.
// The year is 2031, given 2026's parameters: 5% EP, 1% VZ, ESV 1,902.34 a month, advances recommended on
// the 15th. A far-off year keeps the fake clock ahead of the real one, so the session cookie stays valid.
[Collection(nameof(ImportEndpointsTests))]
public sealed class ImportEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Today = new(2031, 6, 1);

    private const string Empty = """{"schemaVersion":1,"settings":null,"clients":[],"transactions":[],"budgetPayments":[]}""";

    private const long EsvMonthKop = 190_234;

    [Fact]
    public async Task Incomes_become_manual_rate_receipts_and_paid_months_become_three_month_payments()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var logBefore = await History(owner);

        var result = await Import(owner, Sample().ToJsonString());

        Assert.Equal(new ImportResponse(false, 3, 0, 6, 0, 0), result);
        var backup = await Backup(owner);

        var uah = Assert.Single(backup.Transactions, row => row.Currency == Currency.UAH);
        Assert.Equal((new DateOnly(2031, 1, 20), 1_000_000L, Money.RateScale, (RateSource?)null, 1_000_000L),
            (uah.ValueDate, uah.AmountMinor, uah.RateE4, uah.RateSource, uah.AmountUahKop));
        Assert.Equal(TransactionKind.Income, uah.Kind);

        var usd = backup.Transactions.Where(row => row.Currency == Currency.USD).OrderBy(row => row.AmountMinor).ToArray();
        Assert.Equal(2, usd.Length);
        Assert.All(usd, row => Assert.Equal((RateSource?)RateSource.Manual, row.RateSource));
        Assert.All(usd, row => Assert.Null(row.RateDate));
        Assert.Equal((100_000L, 412_345, 4_123_450L), (usd[0].AmountMinor, usd[0].RateE4, usd[0].AmountUahKop));
        Assert.Equal("INV-1", usd[0].InvoiceNumber);
        Assert.Equal("first", usd[0].Description);
        var acme = Assert.Single(backup.Clients);
        Assert.Equal("Acme", acme.Name);
        Assert.All(usd, row => Assert.Equal(acme.Id, row.ClientId));

        // A floating-point uah would read 41234.905 as 41234.90499…; the text rounds half up to …91.
        Assert.Equal((100_001L, 4_123_491L), (usd[1].AmountMinor, usd[1].AmountUahKop));

        // January: 5% and 1% of 10,000.00. February: the year-to-date tax on 10,000.00 + 41,234.50
        // + 41,234.91 minus January's.
        var payments = backup.BudgetPayments.OrderBy(row => row.PeriodMonth).ThenBy(row => row.Kind).ToArray();
        Assert.All(payments, row => Assert.Null(row.PeriodQuarter));
        Assert.Equal(
            [
                (1, PaymentKind.SingleTax, 50_000L, new DateOnly(2031, 2, 15)),
                (1, PaymentKind.MilitaryLevy, 10_000L, new DateOnly(2031, 2, 15)),
                (1, PaymentKind.Esv, EsvMonthKop, new DateOnly(2031, 2, 15)),
                (2, PaymentKind.SingleTax, 412_347L, new DateOnly(2031, 3, 15)),
                (2, PaymentKind.MilitaryLevy, 82_469L, new DateOnly(2031, 3, 15)),
                (2, PaymentKind.Esv, EsvMonthKop, new DateOnly(2031, 3, 15)),
            ],
            payments.Select(row => (row.PeriodMonth!.Value, row.Kind, row.AmountKop, row.PaidOn)));

        // A merge, unlike a restore, keeps the per-record history: each imported row is created once.
        var created = (await History(owner)).ExceptBy(logBefore.Select(entry => entry.Id), entry => entry.Id).ToArray();
        Assert.All(created, entry => Assert.Equal(AuditAction.Create, entry.Action));
        Assert.Equal(3, created.Count(entry => entry.Entity == AuditedEntity.Transaction));
        Assert.Equal(6, created.Count(entry => entry.Entity == AuditedEntity.BudgetPayment));
    }

    [Fact]
    public async Task Importing_the_same_file_again_changes_nothing_and_one_more_record_adds_exactly_one()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var file = Sample();

        await Import(owner, file.ToJsonString());
        var afterFirst = await RawBackup(owner);

        var again = await Import(owner, file.ToJsonString());

        Assert.Equal(new ImportResponse(false, 0, 3, 0, 6, 0), again);
        Assert.Equal(afterFirst, await RawBackup(owner));

        file["incomes"]!.AsArray().Add(Income("2031-03-02", "500.00", "UAH", null, "500.00"));
        var extended = await Import(owner, file.ToJsonString());

        Assert.Equal(new ImportResponse(false, 1, 3, 0, 6, 0), extended);
        Assert.Equal(4, (await Backup(owner)).Transactions.Length);
    }

    [Fact]
    public async Task Two_identical_receipts_in_one_file_are_both_imported_once()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var twice = new JsonObject
        {
            ["incomes"] = new JsonArray(
                Income("2031-01-20", "10.00", "UAH", null, "10.00"),
                Income("2031-01-20", "10.00", "UAH", null, "10.00")),
        }.ToJsonString();

        Assert.Equal(new ImportResponse(false, 2, 0, 0, 0, 0), await Import(owner, twice));
        Assert.Equal(new ImportResponse(false, 0, 2, 0, 0, 0), await Import(owner, twice));
        Assert.Equal(2, (await Backup(owner)).Transactions.Length);
    }

    [Fact]
    public async Task A_month_the_owner_already_recorded_a_payment_for_is_not_paid_twice()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var esv = await owner.PostAsJsonAsync(
            "/api/payments",
            new PaymentRequest(new DateOnly(2031, 2, 10), PaymentKind.Esv, EsvMonthKop, 2031, null, 1, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, esv.StatusCode);

        var result = await Import(owner, """{"incomes":[],"mpaid":{"2031-01":true,"2031-02":true}}""");

        // No income: January's EP and VZ accrued nothing, its ESV is already recorded; February pays ESV only.
        Assert.Equal(new ImportResponse(false, 0, 0, 1, 1, 4), result);
        Assert.Equal(2, (await Backup(owner)).BudgetPayments.Length);
    }

    [Fact]
    public async Task A_dry_run_reports_the_counts_and_writes_nothing()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var before = await RawBackup(owner);
        var logBefore = await History(owner);

        var response = await Post(owner, Sample().ToJsonString(), dryRun: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ImportResponse(true, 3, 0, 6, 0, 0), await response.Content.ReadFromJsonAsync<ImportResponse>(Json));
        Assert.Equal(before, await RawBackup(owner));
        Assert.Equal(logBefore.Length, (await History(owner)).Length);
    }

    [Fact]
    public async Task Paid_months_need_a_registration_date_first()
    {
        await using var application = CreateApplication();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Empty);

        var response = await Post(owner, Sample().ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorUnder(response, "mpaid");
        Assert.Empty((await Backup(owner)).Transactions);
    }

    public static TheoryData<string, string, string?> HostileFiles() => new()
    {
        { "root is an array", "[]", "file" },
        { "neither incomes nor mpaid", """{"settings":{}}""", "file" },
        { "unknown top-level field", """{"incomes":[],"extra":1}""", "extra" },
        { "incomes not an array", """{"incomes":{}}""", "incomes" },
        { "income not an object", """{"incomes":[1]}""", "incomes[0]" },
        { "unknown income field", IncomesWith(income => income["id"] = 7), "incomes[0].id" },
        { "missing date", IncomesWith(income => income.Remove("date")), "incomes[0].date" },
        { "date not ISO", IncomesWith(income => income["date"] = "20.01.2026"), "incomes[0].date" },
        { "future date", IncomesWith(income => income["date"] = "2031-06-02"), "incomes[0].date" },
        { "currency outside UAH, USD, EUR", IncomesWith(income => income["currency"] = "GBP"), "incomes[0].currency" },
        { "amount with three decimals", IncomesWith(income => income["amount"] = JsonNode.Parse("10.001")), "incomes[0].amount" },
        { "amount in exponent form", IncomesWith(income => income["amount"] = JsonNode.Parse("1e3")), "incomes[0].amount" },
        { "negative amount", IncomesWith(income => income["amount"] = JsonNode.Parse("-10.00")), "incomes[0].amount" },
        { "amount a boolean", IncomesWith(income => income["amount"] = true), "incomes[0].amount" },
        { "USD without a rate", IncomesWith(income => { income["currency"] = "USD"; income.Remove("rate"); }), "incomes[0].rate" },
        { "UAH with a rate", IncomesWith(income => income["rate"] = "41.00"), "incomes[0].rate" },
        { "uah that amount × rate cannot give", IncomesWith(income => income["uah"] = "10.01"), "incomes[0].uah" },
        { "NUL in client", IncomesWith(income => income["client"] = "Ac\u0000me"), "incomes[0].client" },
        { "NUL in comment", IncomesWith(income => income["comment"] = "\u0000"), "incomes[0].comment" },
        { "client a number", IncomesWith(income => income["client"] = 5), "incomes[0].client" },
        { "too long invoice", IncomesWith(income => income["invoice"] = new string('x', 101)), "incomes[0].invoice" },
        { "mpaid an array", """{"mpaid":[]}""", "mpaid" },
        { "mpaid key not a month", """{"mpaid":{"2031-13":true}}""", "mpaid.2031-13" },
        { "mpaid value not a boolean", """{"mpaid":{"2031-01":1}}""", "mpaid.2031-01" },
        { "mpaid month in the future", """{"mpaid":{"2031-07":true}}""", "mpaid.2031-07" },
        { "mpaid month twice", """{"mpaid":{"2031-01":true,"2031-01":false}}""", "mpaid.2031-01" },
        { "mpaid year without parameters", """{"mpaid":{"2030-12":true}}""", "mpaid.2030-12" },
        { "not JSON", "{\"incomes\":[", null },
    };

    [Theory]
    [MemberData(nameof(HostileFiles))]
    public async Task A_hostile_file_is_rejected_by_field_and_changes_nothing(string name, string file, string? errorKey)
    {
        _ = name;
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        await Import(owner, """{"incomes":[{"date":"2031-01-02","amount":"1.00","currency":"UAH","uah":"1.00"}]}""");
        var before = await RawBackup(owner);

        var response = await Post(owner, file);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (errorKey is not null)
        {
            await AssertErrorUnder(response, errorKey);
        }

        Assert.Equal(before, await RawBackup(owner));
    }

    [Fact]
    public async Task A_future_month_marked_unpaid_is_accepted_and_writes_nothing()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);

        var result = await Import(owner, """{"mpaid":{"2031-01":false,"2031-12":false}}""");

        Assert.Equal(new ImportResponse(false, 0, 0, 0, 0, 0), result);
        Assert.Empty((await Backup(owner)).BudgetPayments);
    }

    // A lookup through DbSet.Local per receipt once made this quadratic: 10,000 named receipts ran past
    // the web proxy's 30 seconds while holding the owner's lock. Growth is checked as a ratio because a
    // shared CI runner is several times slower than a workstation. Tenfold the receipts costs seven to
    // fourteen times the time today and cost about sixtyfold with that lookup, so the bound sits
    // between them. The warm-up is as large as the small import because a first import costs up to twice as
    // much, which would pull the regression's ratio down towards the bound. The absolute cap only
    // catches a runaway.
    [Fact]
    public async Task Import_time_grows_linearly_up_to_the_largest_allowed_file()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        await Import(owner, NamedReceipts(PrototypeFile.MaxIncomes / 10, day: 1));

        var small = await Timed(owner, NamedReceipts(PrototypeFile.MaxIncomes / 10, day: 2));
        var large = await Timed(owner, NamedReceipts(PrototypeFile.MaxIncomes, day: 3));

        Assert.Equal(20, (await Backup(owner)).Clients.Length);
        Assert.True(large < small * 25, $"{PrototypeFile.MaxIncomes / 10} took {small}, {PrototypeFile.MaxIncomes} took {large}");
        Assert.True(large < TimeSpan.FromSeconds(60), $"took {large}");
    }

    private static string NamedReceipts(int count, int day)
    {
        var incomes = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var income = Income($"2031-{i % 5 + 1:00}-{day:00}", $"{i + 1}.00", "UAH", null, $"{i + 1}.00");
            income["client"] = $"Client {i % 20}";
            incomes.Add(income);
        }

        return new JsonObject { ["incomes"] = incomes }.ToJsonString();
    }

    private static async Task<TimeSpan> Timed(HttpClient owner, string file)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await Import(owner, file);
        clock.Stop();
        Assert.Equal(0, result.TransactionsAlreadyPresent);
        return clock.Elapsed;
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);

        var response = await Post(owner, Sample().ToJsonString() + new string(' ', BackupEndpoints.MaxRestoreBytes));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty((await Backup(owner)).Transactions);
    }

    [Fact]
    public async Task Too_many_incomes_are_refused()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        var incomes = new JsonArray();
        for (var i = 0; i <= PrototypeFile.MaxIncomes; i++)
        {
            incomes.Add(Income("2031-01-02", "1.00", "UAH", null, "1.00"));
        }

        var response = await Post(owner, new JsonObject { ["incomes"] = incomes }.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorUnder(response, "incomes");
    }

    [Fact]
    public async Task A_non_json_body_and_a_missing_session_are_refused()
    {
        await using var application = CreateApplication();
        using var owner = await SignedInFresh(application);
        using var anonymous = ApiFixture.CreateClient(application);

        var text = await owner.PostAsync(
            "/api/import/prototype", new StringContent(Sample().ToJsonString(), Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, Sample().ToJsonString())).StatusCode);
    }

    private WebApplicationFactory<Program> CreateApplication() =>
        fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("an import never asks NBU")), Today);

    private static async Task<HttpClient> SignedInFresh(WebApplicationFactory<Program> application)
    {
        var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await Restore(owner, Empty);
        var year = new TaxYearConfigRequest(
            864_700, 500, 100, 2_200, 1_500, 1_167, [85, 100], 19, 40, 10, 15, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2031", year, Json)).StatusCode);
        var settings = new SettingsRequest(
            new DateOnly(2031, 1, 1),
            PaymentMode.MonthlyAdvance,
            EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday],
            "uk",
            "system",
            "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", settings, Json)).StatusCode);
        return owner;
    }

    // Numbers are sent both as JSON numbers and as strings, the two ways a browser prototype stores them.
    private static JsonObject Sample() => new()
    {
        ["settings"] = new JsonObject { ["anything"] = "the prototype kept" },
        ["incomes"] = new JsonArray(
            Income("2031-01-20", "10000.00", "UAH", null, "10000.00"),
            new JsonObject
            {
                ["date"] = "2031-02-10",
                ["amount"] = JsonNode.Parse("1000.00"),
                ["currency"] = "USD",
                ["rate"] = JsonNode.Parse("41.2345"),
                ["uah"] = JsonNode.Parse("41234.5"),
                ["client"] = " Acme ",
                ["invoice"] = "INV-1",
                ["comment"] = "first",
            },
            new JsonObject
            {
                ["date"] = "2031-02-11",
                ["amount"] = "1000.01",
                ["currency"] = "USD",
                ["rate"] = "41.2345",
                ["uah"] = JsonNode.Parse("41234.905"),
                ["client"] = "Acme",
                ["invoice"] = null,
            }),
        ["mpaid"] = new JsonObject { ["2031-01"] = true, ["2031-02"] = true, ["2031-03"] = false },
        ["done"] = new JsonObject { ["2031-Q1"] = true },
    };

    private static JsonObject Income(string date, string amount, string currency, string? rate, string uah)
    {
        var income = new JsonObject { ["date"] = date, ["amount"] = amount, ["currency"] = currency, ["uah"] = uah };
        if (rate is not null)
        {
            income["rate"] = rate;
        }

        return income;
    }

    private static string IncomesWith(Action<JsonObject> change)
    {
        var income = new JsonObject
        {
            ["date"] = "2031-01-20",
            ["amount"] = "10.00",
            ["currency"] = "UAH",
            ["uah"] = "10.00",
        };
        change(income);
        return new JsonObject { ["incomes"] = new JsonArray(income) }.ToJsonString();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string file, bool dryRun = false) =>
        client.PostAsync(
            dryRun ? "/api/import/prototype?dryRun=true" : "/api/import/prototype",
            new StringContent(file, Encoding.UTF8, "application/json"));

    private static async Task<ImportResponse> Import(HttpClient owner, string file)
    {
        var response = await Post(owner, file);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportResponse>(Json))!;
    }

    private static async Task AssertErrorUnder(HttpResponseMessage response, string key)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(key, out _), $"no error under {key}: {errors}");
    }

    private static async Task Restore(HttpClient owner, string file)
    {
        var response = await owner.PostAsync("/api/restore", new StringContent(file, Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> RawBackup(HttpClient owner) => await owner.GetStringAsync("/api/backup");

    private static async Task<BackupDocument> Backup(HttpClient owner) =>
        JsonSerializer.Deserialize<BackupDocument>(await RawBackup(owner), Json)!;

    private static async Task<AuditEntryResponse[]> History(HttpClient owner) =>
        (await owner.GetFromJsonAsync<AuditEntryResponse[]>("/api/audit", Json))!;
}

// Run alone, after every parallel collection: the growth test times two imports, and the other
// classes' tests competing for a CI runner's four cores slowed the large one more than the small one,
// pushing a linear import past the bound.
[CollectionDefinition(nameof(ImportEndpointsTests), DisableParallelization = true)]
public sealed class ImportEndpointsCollection;
