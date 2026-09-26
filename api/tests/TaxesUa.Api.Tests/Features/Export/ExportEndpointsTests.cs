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

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/export/transactions.csv?year=2030");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Year_out_of_range_is_rejected()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);

        var response = await client.GetAsync("/api/export/transactions.csv?year=1999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "year");
    }

    [Fact]
    public async Task Csv_export_is_scoped_to_the_owner_and_the_year()
    {
        const int year = 2031;
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
        const int year = 2032;
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
