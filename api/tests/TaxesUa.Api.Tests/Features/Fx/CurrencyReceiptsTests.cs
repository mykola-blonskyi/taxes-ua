using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Tests.Features.Fx;

// One database for the class, shared by the FxRates cache and the ledger, so every test uses its own
// year.
public sealed class CurrencyReceiptsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Today = new(2040, 1, 1);

    [Fact]
    public async Task An_nbu_rate_is_fixed_when_recorded()
    {
        var valueDate = new DateOnly(2032, 3, 2);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20320302"] = StubNbuHandler.Row("USD", valueDate, "44.9729"),
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var created = await client.PostAsJsonAsync(
            "/api/transactions", Body(Currency.USD, 10_000, valueDate), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal(449_729, receipt.RateE4);
        Assert.Equal(449_729, receipt.AmountUahKop);
        Assert.Equal(RateSource.Nbu, receipt.RateSource);
        Assert.Equal(valueDate, receipt.RateDate);

        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cached = await database.FxRates.SingleAsync(
                rate => rate.Currency == Currency.USD && rate.Date == valueDate);
            cached.RateE4 = 500_000;
            await database.SaveChangesAsync();
        }

        var listed = await client.GetFromJsonAsync<TransactionListResponse>("/api/transactions?year=2032", Json);
        Assert.Equal(449_729, listed!.Items.Single().AmountUahKop);

        var updated = await client.PutAsJsonAsync(
            $"/api/transactions/{receipt.Id}",
            Body(Currency.USD, 10_000, valueDate, description: "consulting"),
            Json);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var edited = (await updated.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal("consulting", edited.Description);
        Assert.Equal(449_729, edited.RateE4);
        Assert.Equal(449_729, edited.AmountUahKop);
        Assert.Equal(valueDate, edited.RateDate);
        Assert.Single(nbu.Requests);
    }

    [Fact]
    public async Task A_rate_cached_by_a_concurrent_request_does_not_fail_the_receipt()
    {
        var valueDate = new DateOnly(2037, 5, 4);
        var nbu = new StubNbuHandler(_ =>
        {
            // Lands between the request's cache miss and its own insert, as a concurrent request would.
            using var scope = fixture.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.FxRates.Add(new FxRate
            {
                Currency = Currency.USD,
                Date = valueDate,
                RateE4 = 449_729,
                RateDate = valueDate,
                FetchedAt = DateTimeOffset.UtcNow,
            });
            database.SaveChanges();
            return StubNbuHandler.Json(StubNbuHandler.Row("USD", valueDate, "44.9729"));
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var created = await client.PostAsJsonAsync("/api/transactions", Body(Currency.USD, 10_000, valueDate), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal(449_729, receipt.AmountUahKop);
        Assert.Single(nbu.Requests);
    }

    [Fact]
    public async Task A_manual_rate_is_marked_manual_and_rounds_half_up()
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var created = await client.PostAsJsonAsync(
            "/api/transactions", Body(Currency.EUR, 10_001, new DateOnly(2033, 1, 10), manualRateE4: 425_000), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal(RateSource.Manual, receipt.RateSource);
        Assert.Null(receipt.RateDate);
        Assert.Equal(425_000, receipt.RateE4);
        Assert.Equal(425_043, receipt.AmountUahKop);
        Assert.Empty(nbu.Requests);
    }

    [Fact]
    public async Task Without_nbu_a_foreign_receipt_needs_a_manual_rate()
    {
        var nbu = new StubNbuHandler(_ => throw new HttpRequestException("connection refused"));
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var valueDate = new DateOnly(2034, 2, 1);

        var withoutRate = await client.PostAsJsonAsync("/api/transactions", Body(Currency.USD, 10_000, valueDate), Json);
        var withRate = await client.PostAsJsonAsync(
            "/api/transactions", Body(Currency.USD, 10_000, valueDate, manualRateE4: 410_000), Json);

        Assert.Equal(HttpStatusCode.BadGateway, withoutRate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withRate.StatusCode);
        var listed = await client.GetFromJsonAsync<TransactionListResponse>("/api/transactions?year=2034", Json);
        Assert.Single(listed!.Items);
    }

    [Fact]
    public async Task An_edit_to_another_date_takes_that_dates_rate()
    {
        var first = new DateOnly(2035, 4, 1);
        var second = new DateOnly(2035, 4, 2);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20350401"] = StubNbuHandler.Row("EUR", first, "50.0000"),
            ["20350402"] = StubNbuHandler.Row("EUR", second, "51.0000"),
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var created = await client.PostAsJsonAsync("/api/transactions", Body(Currency.EUR, 100, first), Json);
        var receipt = (await created.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        var updated = await client.PutAsJsonAsync(
            $"/api/transactions/{receipt.Id}", Body(Currency.EUR, 100, second), Json);

        var edited = (await updated.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
        Assert.Equal(510_000, edited.RateE4);
        Assert.Equal(second, edited.RateDate);
        Assert.Equal(5_100, edited.AmountUahKop);
    }

    [Theory]
    [InlineData(nameof(Currency.UAH), 10_000L, 410_000, "manualRateE4")]
    [InlineData(nameof(Currency.USD), 10_000L, 0, "manualRateE4")]
    [InlineData(nameof(Currency.USD), 10_000L, 10_000_001, "manualRateE4")]
    [InlineData(nameof(Currency.USD), 100_000_000_000_000L, 450_000, "amountMinor")]
    public async Task An_invalid_rate_or_an_oversized_hryvnia_amount_is_rejected(
        string currencyName, long amountMinor, int manualRateE4, string key)
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var body = Body(Enum.Parse<Currency>(currencyName), amountMinor, new DateOnly(2036, 1, 1), manualRateE4: manualRateE4);

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(key, out _));
        Assert.Empty(nbu.Requests);
    }

    private static Dictionary<string, object?> Body(
        Currency currency,
        long amountMinor,
        DateOnly valueDate,
        int? manualRateE4 = null,
        string? description = null) => new()
    {
        ["valueDate"] = valueDate.ToString("yyyy-MM-dd"),
        ["amountMinor"] = amountMinor,
        ["currency"] = currency.ToString(),
        ["manualRateE4"] = manualRateE4,
        ["kind"] = nameof(TransactionKind.Income),
        ["nonIncomeReason"] = null,
        ["clientName"] = null,
        ["invoiceNumber"] = null,
        ["description"] = description,
    };
}
