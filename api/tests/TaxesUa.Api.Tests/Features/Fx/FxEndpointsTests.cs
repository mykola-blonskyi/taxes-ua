using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Fx;

// The FxRates cache lives in the class's one database, so every test asks for its own dates.
public sealed class FxEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Today = new(2040, 1, 1);

    [Fact]
    public async Task A_date_without_a_rate_falls_back_to_the_previous_day_and_is_then_served_from_the_cache()
    {
        var saturday = new DateOnly(2031, 1, 4);
        var friday = new DateOnly(2031, 1, 3);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20310103"] = StubNbuHandler.Row("USD", friday, "44.9729"),
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var first = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=USD&date=2031-01-04", Json);
        var requestsAfterFirst = nbu.Requests.Count;
        var second = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=USD&date=2031-01-04", Json);

        var expected = new FxRateResponse(Currency.USD, saturday, 449_729, friday);
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        Assert.Equal(2, requestsAfterFirst);
        Assert.Equal(requestsAfterFirst, nbu.Requests.Count);
    }

    [Fact]
    public async Task A_published_weekday_answers_with_its_own_date()
    {
        var date = new DateOnly(2031, 3, 5);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20310305"] = StubNbuHandler.Row("EUR", date, "51.1234"),
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var rate = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=EUR&date=2031-03-05", Json);

        Assert.Equal(new FxRateResponse(Currency.EUR, date, 511_234, date), rate);
        Assert.Single(nbu.Requests);
        Assert.Contains("valcode=EUR", nbu.Requests.Single().Query);
    }

    [Fact]
    public async Task A_rate_with_five_decimals_is_rounded_half_away_from_zero()
    {
        var date = new DateOnly(2031, 5, 6);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20310506"] = StubNbuHandler.Row("USD", date, "44.97285"),
        });
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var rate = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=USD&date=2031-05-06", Json);

        Assert.Equal(449_729, rate!.RateE4);
    }

    [Theory]
    [InlineData("2031-02-10", typeof(HttpRequestException))]
    [InlineData("2031-02-11", typeof(TaskCanceledException))]
    public async Task A_network_failure_or_timeout_answers_502(string date, Type exceptionType)
    {
        var nbu = new StubNbuHandler(_ => throw (Exception)Activator.CreateInstance(exceptionType)!);
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await client.GetAsync($"/api/fx?currency=USD&date={date}");

        await AssertBadGateway(response, "fx_service_unavailable");
        Assert.Single(nbu.Requests);
    }

    [Theory]
    [InlineData("2031-04-01", "<html>")]
    [InlineData("2031-04-02", """[{"rate":"abc","cc":"USD","exchangedate":"02.04.2031"}]""")]
    [InlineData("2031-04-03", """[{"rate":44.9729,"cc":"EUR","exchangedate":"03.04.2031"}]""")]
    [InlineData("2031-04-04", """[{"rate":44.9729,"cc":"USD","exchangedate":"01.04.2031"}]""")]
    [InlineData("2031-04-05", """[{"rate":0,"cc":"USD","exchangedate":"05.04.2031"}]""")]
    [InlineData("2031-04-06", """{"rate":44.9729}""")]
    public async Task An_unusable_answer_answers_502_without_falling_back(string date, string body)
    {
        var nbu = new StubNbuHandler(_ => StubNbuHandler.Json(body));
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await client.GetAsync($"/api/fx?currency=USD&date={date}");

        await AssertBadGateway(response, "fx_service_unavailable");
        Assert.Single(nbu.Requests);
    }

    [Fact]
    public async Task A_server_error_answers_502()
    {
        var nbu = new StubNbuHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await client.GetAsync("/api/fx?currency=USD&date=2031-04-10");

        await AssertBadGateway(response, "fx_service_unavailable");
    }

    [Fact]
    public async Task Seven_empty_days_answer_502_naming_the_currency()
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await client.GetAsync("/api/fx?currency=EUR&date=2031-07-20");

        await AssertBadGateway(response, "fx_rate_not_published");
        Assert.Equal(7, nbu.Requests.Count);
    }

    [Fact]
    public async Task A_fallback_for_a_future_date_is_answered_but_not_cached()
    {
        var today = new DateOnly(2031, 6, 10);
        var future = new DateOnly(2031, 6, 14);
        var published = new DateOnly(2031, 6, 13);
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>
        {
            ["20310613"] = StubNbuHandler.Row("USD", published, "41.0000"),
        });
        await using var application = fixture.CreateApplication(nbu, today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var first = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=USD&date=2031-06-14", Json);
        var second = await client.GetFromJsonAsync<FxRateResponse>("/api/fx?currency=USD&date=2031-06-14", Json);

        Assert.Equal(new FxRateResponse(Currency.USD, future, 410_000, published), first);
        Assert.Equal(first, second);
        Assert.Equal(4, nbu.Requests.Count);

        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.FxRates.AnyAsync(rate => rate.Currency == Currency.USD && rate.Date == future));
    }

    [Theory]
    [InlineData("/api/fx?currency=UAH&date=2031-08-01", "currency")]
    [InlineData("/api/fx?currency=7&date=2031-08-01", "currency")]
    [InlineData("/api/fx?currency=USD&date=1999-12-31", "date")]
    public async Task An_invalid_query_is_rejected_before_any_nbu_call(string url, string key)
    {
        var nbu = StubNbuHandler.ByDate(new Dictionary<string, string>());
        await using var application = fixture.CreateApplication(nbu, Today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        ProblemAssert.Rejects(document.RootElement, key);
        Assert.Empty(nbu.Requests);
    }

    [Fact]
    public async Task An_anonymous_caller_is_rejected()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/fx?currency=USD&date=2031-08-02");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertBadGateway(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }
}
