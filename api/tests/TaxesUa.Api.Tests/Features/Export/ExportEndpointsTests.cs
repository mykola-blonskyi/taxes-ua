using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Tests.Features.Export;

// ApiFixture is shared across this class's tests, so every test isolates by year.
public sealed class ExportEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("csv")]
    [InlineData("pdf")]
    public async Task Unauthenticated_request_is_rejected(string format)
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync($"/api/export/transactions.{format}?year=2030");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("pdf")]
    public async Task Year_out_of_range_is_rejected(string format)
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);

        var response = await client.GetAsync($"/api/export/transactions.{format}?year=1999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "year");
    }

    [Fact]
    public async Task Csv_export_is_scoped_to_the_owner_and_the_year()
    {
        const int year = 2003;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);

        await Create(owner, valueDate: new DateOnly(year, 1, 5));
        await Create(owner, kind: TransactionKind.RefundToClient, valueDate: new DateOnly(year, 2, 5));
        await Create(owner, valueDate: new DateOnly(year, 3, 5));
        await Create(owner, valueDate: new DateOnly(year + 1, 1, 5));
        await Create(other, valueDate: new DateOnly(year, 4, 5));

        var response = await owner.GetAsync($"/api/export/transactions.csv?year={year}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"transactions-{year}.csv", response.Content.Headers.ContentDisposition!.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.Equal(
            ["100,00", "-100,00", "100,00"],
            lines.Skip(1).Select(line => line.Split(';')[6]));
    }

    [Fact]
    public async Task Xlsx_export_is_scoped_to_the_owner_and_the_year()
    {
        const int year = 2005;
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        await Create(owner, valueDate: new DateOnly(year, 1, 5));
        await Create(owner, valueDate: new DateOnly(year, 2, 5));
        await Create(owner, valueDate: new DateOnly(year, 3, 5));

        var response = await owner.GetAsync($"/api/export/transactions.xlsx?year={year}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"transactions-{year}.xlsx", response.Content.Headers.ContentDisposition!.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheetData = document.WorkbookPart!.WorksheetParts.Single().Worksheet!
            .Elements<DocumentFormat.OpenXml.Spreadsheet.SheetData>().Single();

        Assert.Equal(4, sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>().Count());
    }

    [Fact]
    public async Task Pdf_export_is_scoped_to_the_owner_and_the_year_and_totals_income_only()
    {
        const int year = 2007;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);

        await Create(owner, amountMinor: 12_345, valueDate: new DateOnly(year, 1, 5));
        await Create(owner, kind: TransactionKind.RefundToClient, amountMinor: 2_345, valueDate: new DateOnly(year, 2, 5));
        await Create(owner, kind: TransactionKind.OwnTransfer, amountMinor: 77_700, valueDate: new DateOnly(year, 3, 5), nonIncomeReason: "З картки ПриватБанку");
        await Create(owner, valueDate: new DateOnly(year + 1, 1, 5));
        await Create(other, amountMinor: 99_900, valueDate: new DateOnly(year, 4, 5));

        var response = await owner.GetAsync($"/api/export/transactions.pdf?year={year}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"transactions-{year}.pdf", response.Content.Headers.ContentDisposition!.FileName);

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, pdf.NumberOfPages);
        var text = string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text.Replace('\u00A0', ' ')));
        var screen = await owner.GetFromJsonAsync<JsonElement>($"/api/transactions?year={year}", Json);
        var screenTotalKop = screen.GetProperty("totalIncomeKop").GetInt64();
        Assert.NotEqual(12_345 - 2_345 + 77_700, screenTotalKop);
        Assert.Contains($"Дохід за {year}: {screenTotalKop / 100},{screenTotalKop % 100:00} грн", text);
        Assert.Contains("123,45", text);
        Assert.Contains("-23,45", text);
        Assert.Contains("777,00", text);
        Assert.Contains($"05.03.{year}", text);
        Assert.DoesNotContain($"05.01.{year + 1}", text);
        Assert.DoesNotContain("999,00", text);
    }

    private static async Task AssertErrorKey(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(key, out _));
    }

    private async Task<TransactionResponse> Create(
        HttpClient client,
        TransactionKind kind = TransactionKind.Income,
        long amountMinor = 10_000,
        DateOnly valueDate = default,
        string? nonIncomeReason = null)
    {
        var body = Body(kind: kind, amountMinor: amountMinor, valueDate: valueDate, nonIncomeReason: nonIncomeReason);

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private static Dictionary<string, object?> Body(
        TransactionKind kind,
        long amountMinor,
        DateOnly valueDate,
        string? nonIncomeReason) => new()
    {
        ["valueDate"] = valueDate.ToString("yyyy-MM-dd"),
        ["amountMinor"] = amountMinor,
        ["currency"] = "UAH",
        ["manualRateE4"] = null,
        ["kind"] = kind.ToString(),
        ["nonIncomeReason"] = nonIncomeReason,
        ["clientName"] = null,
        ["invoiceNumber"] = null,
        ["description"] = null,
        ["refundsTransactionId"] = null,
    };

    private async Task<HttpClient> SignIn(string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return client;
    }
}
