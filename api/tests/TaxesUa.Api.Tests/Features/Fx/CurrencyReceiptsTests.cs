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
        ProblemAssert.Rejects(document.RootElement, key);
        Assert.Empty(nbu.Requests);
    }

    [Fact]
    public async Task A_linked_refund_is_capped_in_the_receipts_own_currency_not_in_hryvnia()
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var receipt = await Created(client, Body(Currency.USD, 10_000, new DateOnly(2038, 2, 2), manualRateE4: 400_000));

        // At the higher refund-day rate each refund is worth more hryvnia than its share of the
        // receipt, so only a comparison in dollars lets exactly 100.00 USD through.
        var first = await client.PostAsJsonAsync("/api/transactions", Refund(Currency.USD, 6_000, receipt.Id), Json);
        var rest = await client.PostAsJsonAsync("/api/transactions", Refund(Currency.USD, 4_000, receipt.Id), Json);
        var over = await client.PostAsJsonAsync("/api/transactions", Refund(Currency.USD, 1, receipt.Id), Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
        await AssertRejected(over, "refundsTransactionId");
    }

    [Fact]
    public async Task A_refund_in_another_currency_cannot_link_to_the_receipt()
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var uahReceipt = await Created(client, Body(Currency.UAH, 1_000_000, new DateOnly(2038, 3, 3)));

        var response = await client.PostAsJsonAsync(
            "/api/transactions", Refund(Currency.USD, 1_000, uahReceipt.Id), Json);

        await AssertRejected(response, "refundsTransactionId");
    }

    [Fact]
    public async Task A_receipt_with_linked_refunds_keeps_its_currency()
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var valueDate = new DateOnly(2038, 4, 4);
        var receipt = await Created(client, Body(Currency.USD, 10_000, valueDate, manualRateE4: 400_000));
        await Created(client, Refund(Currency.USD, 1_000, receipt.Id));

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{receipt.Id}", Body(Currency.EUR, 10_000, valueDate, manualRateE4: 450_000), Json);

        await AssertRejected(response, "currency");
    }

    private static Dictionary<string, object?> Refund(Currency currency, long amountMinor, Guid receiptId) =>
        Body(
            currency,
            amountMinor,
            new DateOnly(2038, 5, 5),
            manualRateE4: currency == Currency.UAH ? null : 450_000,
            kind: TransactionKind.RefundToClient,
            refundsTransactionId: receiptId);

    private static async Task<TransactionResponse> Created(HttpClient client, Dictionary<string, object?> body)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private static async Task AssertRejected(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        ProblemAssert.Rejects(document.RootElement, key);
    }

    // The sync and restore take the owner's lock for whole windows and files, so an edit that held it
    // while NBU answered would stall them behind a slow bank, and be stalled by them in turn.
    [Fact]
    public async Task An_edit_fetches_a_new_rate_before_it_takes_the_owners_lock()
    {
        var recorded = new DateOnly(2039, 4, 1);
        var moved = new DateOnly(2039, 4, 4);
        var lockFreeWhileNbuAnswered = new List<bool>();
        var nbu = new StubNbuHandler(request =>
        {
            var date = request.RequestUri!.Query.Contains("date=20390404", StringComparison.Ordinal) ? moved : recorded;
            if (date == moved)
            {
                using var scope = fixture.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ownerId = database.Users.Single(user => user.Email == ApiFixture.AllowedEmail).Id;
                lockFreeWhileNbuAnswered.Add(database.Database
                    .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtext({ownerId})) AS \"Value\"")
                    .AsEnumerable()
                    .Single());
            }

            return StubNbuHandler.Json(StubNbuHandler.Row("USD", date, "41.0000"));
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var created = await client.PostAsJsonAsync("/api/transactions", Body(Currency.USD, 10_000, recorded), Json);
        var receipt = (await created.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;

        var updated = await client.PutAsJsonAsync($"/api/transactions/{receipt.Id}", Body(Currency.USD, 10_000, moved), Json);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal([true], lockFreeWhileNbuAnswered);
    }

    private static Dictionary<string, object?> Body(
        Currency currency,
        long amountMinor,
        DateOnly valueDate,
        int? manualRateE4 = null,
        string? description = null,
        TransactionKind kind = TransactionKind.Income,
        Guid? refundsTransactionId = null) => new()
    {
        ["valueDate"] = valueDate.ToString("yyyy-MM-dd"),
        ["amountMinor"] = amountMinor,
        ["currency"] = currency.ToString(),
        ["manualRateE4"] = manualRateE4,
        ["kind"] = kind.ToString(),
        ["nonIncomeReason"] = null,
        ["clientName"] = null,
        ["invoiceNumber"] = null,
        ["description"] = description,
        ["refundsTransactionId"] = refundsTransactionId,
    };
}
