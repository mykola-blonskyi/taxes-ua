using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Archive;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Archive;

// ApiFixture is an IClassFixture, so this class owns its database. xUnit fixes no order between the
// tests, so each one works in a year of its own. The clock is the real one, so every year here is past:
// a quarter's file is servable once the quarter has ended and the file was built after that.
public sealed class ArchiveEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_year_lists_its_documents_and_every_link_downloads()
    {
        const int year = 2012;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var userId = await UserId(ApiFixture.AllowedEmail);
        var issued = await SeedInvoice(userId, year, InvoiceStatus.Issued, 1, new DateOnly(year, 2, 10));
        var cancelled = await SeedInvoice(userId, year, InvoiceStatus.Cancelled, 2, new DateOnly(year, 3, 1));
        await SeedInvoice(userId, year, InvoiceStatus.Draft, null, new DateOnly(year, 4, 1));
        await SeedInvoice(userId, year + 1, InvoiceStatus.Issued, 1, new DateOnly(year + 1, 1, 10));
        await SeedFile(userId, year, 1, DeclarationType.Reporting, annex: false);
        await SeedFile(userId, year, 4, DeclarationType.Reporting, annex: true);
        await SeedFiling(userId, year, 1, new DateOnly(year, 4, 20));
        await Post(owner, new PaymentRequest(new DateOnly(year, 4, 15), PaymentKind.Esv, 190_234, year, 1, null, null));
        await PostIncome(owner, new DateOnly(year, 2, 5));

        var archive = await Get(owner, year);

        Assert.Equal(year, archive.Year);
        Assert.Equal(
            [
                ($"invoice:{issued}", "2012-001_Acme_2012.pdf", InvoiceStatus.Issued),
                ($"invoice:{cancelled}", "2012-002_Acme_2012.pdf", InvoiceStatus.Cancelled),
            ],
            archive.Invoices.Select(invoice => (invoice.Id, invoice.Name, invoice.Status)));
        Assert.Equal([1, 4], archive.Quarters.Select(quarter => quarter.Quarter));
        Assert.Equal(["declaration:2012-1-Reporting"], archive.Quarters[0].Files.Select(file => file.Id));
        Assert.Equal(["declaration:2012-4-Reporting", "annex:2012-4-Reporting"], archive.Quarters[1].Files.Select(file => file.Id));
        Assert.Null(archive.Quarters[1].Filed);
        Assert.Equal(new DateOnly(year, 4, 20), archive.Quarters[0].Filed!.FiledOn);
        Assert.Equal(
            [$"statement:{year}:csv", $"statement:{year}:xlsx", $"statement:{year}:pdf"],
            archive.Statements.Select(item => item.Id));
        Assert.Equal([$"payments:{year}:csv"], archive.Payments.Select(item => item.Id));

        var items = archive.Invoices.Select(invoice => new ArchiveItem(invoice.Id, invoice.Name, invoice.Url))
            .Concat(archive.Quarters.SelectMany(quarter => quarter.Files.Select(file => new ArchiveItem(file.Id, file.Name, file.Url))))
            .Concat(archive.Statements)
            .Concat(archive.Payments)
            .ToArray();
        Assert.Equal(items.Length, items.Select(item => item.Id).Distinct().Count());
        foreach (var item in items)
        {
            var download = await owner.GetAsync(item.Url);

            Assert.True(download.StatusCode == HttpStatusCode.OK, $"{item.Id} at {item.Url} answered {download.StatusCode}");
            var disposition = download.Content.Headers.ContentDisposition!;
            Assert.Equal(item.Name, disposition.FileNameStar ?? disposition.FileName!.Trim('"'));
            Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task A_year_with_nothing_reads_as_empty_sections()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var archive = await Get(owner, 2013);

        Assert.Equal(2013, archive.Year);
        Assert.Empty(archive.Invoices);
        Assert.Empty(archive.Quarters);
        Assert.Empty(archive.Statements);
        Assert.Empty(archive.Payments);
    }

    [Fact]
    public async Task A_quarter_with_only_a_filing_mark_is_listed_without_files()
    {
        const int year = 2014;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await SeedFiling(await UserId(ApiFixture.AllowedEmail), year, 2, new DateOnly(year, 7, 9));

        var quarters = (await Get(owner, year)).Quarters;

        var quarter = Assert.Single(quarters);
        Assert.Equal(2, quarter.Quarter);
        Assert.Equal(new DateOnly(year, 7, 9), quarter.Filed!.FiledOn);
        Assert.Empty(quarter.Files);
    }

    [Fact]
    public async Task A_file_built_before_its_quarter_ended_is_not_listed_because_its_download_refuses_it()
    {
        const int year = 2015;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var userId = await UserId(ApiFixture.AllowedEmail);
        await SeedFile(userId, year, 1, DeclarationType.Reporting, annex: false, generatedAt: new DateTimeOffset(year, 3, 20, 9, 0, 0, TimeSpan.Zero));
        await SeedFile(userId, year, 2, DeclarationType.Reporting, annex: false);

        var archive = await Get(owner, year);

        var quarter = Assert.Single(archive.Quarters);
        Assert.Equal(2, quarter.Quarter);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.GetAsync($"/api/declarations/{year}/1/files/Reporting")).StatusCode);
    }

    [Fact]
    public async Task Another_owner_sees_none_of_it_and_every_link_stays_the_owners()
    {
        const int year = 2016;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var userId = await UserId(ApiFixture.AllowedEmail);
        await SeedInvoice(userId, year, InvoiceStatus.Issued, 1, new DateOnly(year, 2, 10));
        await SeedFile(userId, year, 1, DeclarationType.Reporting, annex: false);
        await SeedFiling(userId, year, 1, new DateOnly(year, 4, 20));
        await Post(owner, new PaymentRequest(new DateOnly(year, 4, 15), PaymentKind.Esv, 190_234, year, 1, null, null));
        await PostIncome(owner, new DateOnly(year, 2, 5));
        var archive = await Get(owner, year);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);

        var seen = await Get(other, year);

        Assert.Empty(seen.Invoices);
        Assert.Empty(seen.Quarters);
        Assert.Empty(seen.Statements);
        Assert.Empty(seen.Payments);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(archive.Invoices.Single().Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(archive.Quarters.Single().Files.Single().Url)).StatusCode);
        var register = await other.GetByteArrayAsync(archive.Payments.Single().Url);
        Assert.Single(Encoding.UTF8.GetString(register).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task The_payments_register_lists_the_years_payments_oldest_first_with_a_header()
    {
        const int year = 2017;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await Post(owner, new PaymentRequest(new DateOnly(year, 6, 15), PaymentKind.SingleTax, 1_234_56, year, 2, null, "=cmd; \"x\""));
        await Post(owner, new PaymentRequest(new DateOnly(year, 4, 15), PaymentKind.Esv, 190_234, year, null, 3, null));
        await Post(owner, new PaymentRequest(new DateOnly(year + 1, 1, 15), PaymentKind.Esv, 1_00, year + 1, 1, null, null));

        var response = await owner.GetAsync($"/api/payments/register.csv?year={year}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"payments-{year}.csv", response.Content.Headers.ContentDisposition!.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal(
            [
                "Дата оплати;Платіж;Сума, грн;Період;Примітка",
                $"15.04.{year};ЄСВ;1902,34;{year}-03;",
                $"15.06.{year};Єдиний податок;1234,56;{year} Q2;\"'=cmd; \"\"x\"\"\"",
            ],
            Encoding.UTF8.GetString(bytes.AsSpan(3)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task The_listing_and_the_register_need_a_session_and_a_year_in_range()
    {
        using var visitor = fixture.CreateClient();
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/archive/2012")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/payments/register.csv?year=2012")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/archive/1999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/payments/register.csv?year=2101")).StatusCode);
    }

    [Fact]
    public async Task The_zip_holds_exactly_the_listing_and_a_contents_file_in_the_owners_language()
    {
        const int year = 2018;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var userId = await UserId(ApiFixture.AllowedEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", YearRequest(), Json)).StatusCode);
        await SeedInvoice(userId, year, InvoiceStatus.Issued, 1, new DateOnly(year, 2, 10));
        await SeedInvoice(userId, year, InvoiceStatus.Cancelled, 2, new DateOnly(year, 3, 1));
        await SeedInvoice(userId, year, InvoiceStatus.Draft, null, new DateOnly(year, 4, 1));
        await SeedFile(userId, year, 1, DeclarationType.Reporting, annex: false);
        await SeedFile(userId, year, 4, DeclarationType.Reporting, annex: true);
        await SeedFiling(userId, year, 1, new DateOnly(year, 4, 20));
        await Post(owner, new PaymentRequest(new DateOnly(year, 4, 15), PaymentKind.Esv, 190_234, year, 1, null, null));
        await PostIncome(owner, new DateOnly(year, 2, 5));
        var archive = await Get(owner, year);

        var response = await owner.GetAsync($"/api/archive/{year}/zip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(
            $"archive-{year}-{await KyivToday():yyyy-MM-dd}.zip",
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("private", response.Headers.CacheControl.ToString());
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));

        var listed = archive.Invoices.Select(invoice => $"invoices/{invoice.Name}")
            .Concat(archive.Quarters.SelectMany(quarter => quarter.Files.Select(file => $"declarations/Q{quarter.Quarter}/{file.Name}")))
            .Concat(archive.Statements.Select(item => $"statement/{item.Name}"))
            .Concat(archive.Payments.Select(item => $"payments/{item.Name}"));
        Assert.Equal(
            listed.Append("contents.txt").Order(StringComparer.Ordinal),
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["invoices/2018-001_Acme_2018.pdf", "invoices/2018-002_Acme_2018.pdf"],
            zip.Entries.Where(entry => entry.FullName.StartsWith("invoices/", StringComparison.Ordinal)).Select(entry => entry.FullName));
        foreach (var invoice in archive.Invoices)
        {
            var entry = Bytes(zip, $"invoices/{invoice.Name}");
            Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(entry, 0, 5));
        }

        Assert.Equal("<DECLAR/>"u8.ToArray(), Bytes(zip, "declarations/Q1/F0103309_2018_1.xml"));
        Assert.Equal("<DECLAR/>"u8.ToArray(), Bytes(zip, "declarations/Q4/F0133109_2018_4.xml"));
        Assert.Equal(
            await owner.GetByteArrayAsync($"/api/payments/register.csv?year={year}"),
            Bytes(zip, $"payments/payments-{year}.csv"));

        var contents = Bytes(zip, "contents.txt");
        Assert.True(contents.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var text = Encoding.UTF8.GetString(contents.AsSpan(3));
        Assert.StartsWith("Архів документів за 2018 рік\r\n", text);
        Assert.Contains("Документи за 2018 рік зберігати до ", text);
        Assert.Contains("  invoices/2018-002_Acme_2018.pdf - 2018-002", text);
        Assert.Contains("скасований", text);
        Assert.Contains("1 квартал (подано 20.04.2018)", text);
        Assert.Contains("4 квартал (не подано)", text);
        Assert.DoesNotContain("#", text);
        foreach (var path in zip.Entries.Select(entry => entry.FullName))
        {
            Assert.Contains(path, text);
        }
    }

    [Fact]
    public async Task The_contents_file_follows_the_language_setting_and_says_when_a_section_is_empty()
    {
        const int year = 2019;
        using var owner = await SignIn(ApiFixture.SecondAllowedEmail);
        var language = await owner.PutAsJsonAsync(
            "/api/settings/appearance", new { locale = "ru", chosenAt = DateTimeOffset.UtcNow }, Json);
        Assert.Equal(HttpStatusCode.OK, language.StatusCode);

        var response = await owner.GetAsync($"/api/archive/{year}/zip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(["contents.txt"], zip.Entries.Select(entry => entry.FullName));
        var text = Encoding.UTF8.GetString(Bytes(zip, "contents.txt").AsSpan(3));
        Assert.StartsWith("Архив документов за 2019 год\r\n", text);
        Assert.Contains("срок хранения не определён", text);
        Assert.Equal(4, text.Split("  нет\r\n").Length - 1);
    }

    [Fact]
    public void The_zip_states_the_keep_until_date_as_the_periods_screen_words_it()
    {
        var keepFixed = new KeepUntilResponse(KeepUntilState.Fixed, new DateOnly(2030, 2, 8), null);
        var open = new KeepUntilResponse(KeepUntilState.ExtendedWhileSuspended, new DateOnly(2030, 2, 8), 1095);
        var empty = new ArchiveContents([], [], false, false);
        var today = new DateOnly(2026, 10, 13);

        Assert.Contains(
            "Документи за 2026 рік зберігати до 08.02.2030\r\n",
            YearZip.TableOfContents(2026, empty, "uk", today, keepFixed));
        Assert.Contains(
            "Документи за 2026 рік зберігати не менше ніж до 08.02.2030. Строк продовжено на час воєнного стану: після його припинення або скасування ще 1095 днів.",
            YearZip.TableOfContents(2026, empty, "uk", today, open));
        Assert.Contains(
            "Документы за 2026 год хранить не менее чем до 08.02.2030. Срок продлён на время военного положения: после его прекращения или отмены ещё 1 день.",
            YearZip.TableOfContents(2026, empty, "ru", today, open with { DaysAfterSuspension = 1 }));
        Assert.Contains("ще 22 дні.", YearZip.TableOfContents(2026, empty, "uk", today, open with { DaysAfterSuspension = 22 }));
    }

    [Fact]
    public async Task Another_owner_gets_a_zip_without_the_owners_documents()
    {
        const int year = 2020;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await SeedInvoice(await UserId(ApiFixture.AllowedEmail), year, InvoiceStatus.Issued, 1, new DateOnly(year, 2, 10));
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);

        var owners = new ZipArchive(new MemoryStream(await owner.GetByteArrayAsync($"/api/archive/{year}/zip")));
        var others = new ZipArchive(new MemoryStream(await other.GetByteArrayAsync($"/api/archive/{year}/zip")));

        Assert.Contains(owners.Entries, entry => entry.FullName.StartsWith("invoices/", StringComparison.Ordinal));
        Assert.Equal(["contents.txt"], others.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public async Task The_zip_needs_a_session_and_a_year_in_range()
    {
        using var visitor = fixture.CreateClient();
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/archive/2012/zip")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/archive/1999/zip")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/archive/2101/zip")).StatusCode);
    }

    private static byte[] Bytes(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private async Task<DateOnly> KyivToday()
    {
        await using var scope = fixture.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TimeProvider>().TodayInKyiv();
    }

    private static TaxYearConfigRequest YearRequest() => new(
        MinWageKop: 137_800,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 0,
        EsvRateBp: 2200,
        ExcessRateBp: 1500,
        IncomeLimitMinWages: 1167,
        LimitWarnThresholdsPct: [85],
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        AdvanceRecommendedDay: 15,
        Group3ApplicationDays: 10,
        Holidays: [],
        Source: "a test source");

    private async Task<ArchiveResponse> Get(HttpClient client, int year)
    {
        var response = await client.GetAsync($"/api/archive/{year}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ArchiveResponse>(Json))!;
    }

    private static async Task Post(HttpClient client, PaymentRequest body) =>
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/payments", body, Json)).StatusCode);

    private static async Task PostIncome(HttpClient client, DateOnly valueDate)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            new Dictionary<string, object?>
            {
                ["valueDate"] = valueDate.ToString("yyyy-MM-dd"),
                ["amountMinor"] = 10_000,
                ["currency"] = "UAH",
                ["manualRateE4"] = null,
                ["kind"] = TransactionKind.Income.ToString(),
                ["nonIncomeReason"] = null,
                ["clientName"] = null,
                ["invoiceNumber"] = null,
                ["description"] = null,
                ["refundsTransactionId"] = null,
            },
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpClient> SignIn(string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(login.Headers.Location)).StatusCode);

        return client;
    }

    private async Task<string> UserId(string email)
    {
        await using var scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<Guid> SeedInvoice(string userId, int year, InvoiceStatus status, int? sequence, DateOnly issued)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Acme {year}";
        var client = database.Clients.FirstOrDefault(row => row.UserId == userId && row.Name == name)
            ?? database.Clients.Add(new Client { Id = Guid.NewGuid(), UserId = userId, Name = name }).Entity;
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientId = client.Id,
            Status = status,
            NumberYear = sequence is null ? null : year,
            NumberSequence = sequence,
            IssueDate = issued,
            DueDate = issued.AddDays(14),
            Currency = Currency.EUR,
            Lines = [new InvoiceLine("Consulting", "Консультації", InvoiceUnit.Hour, 10_000, 100_00)],
            TotalMinor = 1_000_00,
            CancelReason = status == InvoiceStatus.Cancelled ? "Duplicate" : null,
            Snapshot = status == InvoiceStatus.Draft ? null : new InvoiceSnapshot(
                new InvoiceSeller("ФОП Тест Тестович", "FOP Test Testovych", "1234567890", "Київ", "Kyiv"),
                new InvoiceBuyer(name, "1 Hauptstraße, Berlin", "DE", "Germany", null, null),
                new InvoicePayment(InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "", "", ""),
                new InvoiceClauses(
                    InvoicingDefaults.AcceptanceEn, InvoicingDefaults.AcceptanceUk, InvoicingDefaults.FeesEn,
                    InvoicingDefaults.FeesUk, InvoicingDefaults.TaxStatusEn, InvoicingDefaults.TaxStatusUk)),
            CreatedAt = new DateTimeOffset(issued, TimeOnly.MinValue, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(issued, TimeOnly.MinValue, TimeSpan.Zero),
        };
        database.Invoices.Add(invoice);
        await database.SaveChangesAsync();

        return invoice.Id;
    }

    private async Task SeedFile(
        string userId, int year, int quarter, DeclarationType type, bool annex, DateTimeOffset? generatedAt = null)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.DeclarationFiles.Add(new DeclarationFile
        {
            UserId = userId,
            Year = year,
            Quarter = quarter,
            Type = type,
            FileName = $"F0103309_{year}_{quarter}.xml",
            Content = "<DECLAR/>"u8.ToArray(),
            AnnexFileName = annex ? $"F0133109_{year}_{quarter}.xml" : null,
            AnnexContent = annex ? "<DECLAR/>"u8.ToArray() : null,
            GeneratedAt = generatedAt ?? new DateTimeOffset(year, 3 * quarter, 28, 9, 0, 0, TimeSpan.Zero).AddMonths(1),
        });
        await database.SaveChangesAsync();
    }

    private async Task SeedFiling(string userId, int year, int quarter, DateOnly filedOn)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.DeclarationFilings.Add(new DeclarationFiling
        {
            UserId = userId,
            Year = year,
            Quarter = quarter,
            FiledOn = filedOn,
            Type = DeclarationType.Reporting,
            FiledIncomeKop = 10_000,
            CreatedAt = new DateTimeOffset(filedOn, TimeOnly.MinValue, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(filedOn, TimeOnly.MinValue, TimeSpan.Zero),
        });
        await database.SaveChangesAsync();
    }
}
