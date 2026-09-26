using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.TaxYears;

// Every mutating test owns a year no other test in the assembly touches, so nothing here depends on
// the order the shared tax year table is written in. Only the seed and the clone read 2026.
public sealed class TaxYearEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_seeded_year_carries_the_documented_parameters()
    {
        using var client = await SignIn();

        var config = await client.GetFromJsonAsync<TaxYearConfigResponse>("/api/tax-years/2026");

        Assert.NotNull(config);
        Assert.Equal(2026, config.Year);
        Assert.Equal(864_700L, config.MinWageKop);
        Assert.Equal(500, config.SingleTaxRateBp);
        Assert.Equal(100, config.MilitaryLevyRateBp);
        Assert.Equal(2200, config.EsvRateBp);
        Assert.Equal(1500, config.ExcessRateBp);
        Assert.Equal(190_234L, config.EsvMonthlyKop);
        Assert.Equal(1167, config.IncomeLimitMinWages);
        Assert.Equal(1_009_104_900L, config.IncomeLimitKop);
        Assert.Equal(new[] { 85, 100 }, config.LimitWarnThresholdsPct);
        Assert.Equal(19, config.EsvDeadlineDay);
        Assert.Equal(40, config.DeclarationDays);
        Assert.Equal(10, config.TaxPaymentDaysAfterDeclaration);
        Assert.Equal(15, config.AdvanceRecommendedDay);
        Assert.Empty(config.Holidays);
        Assert.False(string.IsNullOrWhiteSpace(config.Source), "the seeded row cites no legal source");
        Assert.Null(config.VerifiedAt);
    }

    [Fact]
    public async Task The_list_carries_the_seeded_year_ordered_by_year()
    {
        using var client = await SignIn();

        var configs = await client.GetFromJsonAsync<TaxYearConfigResponse[]>("/api/tax-years");

        Assert.NotNull(configs);
        Assert.Contains(configs, config => config.Year == 2026);
        Assert.Equal(configs.Select(config => config.Year).Order(), configs.Select(config => config.Year));
    }

    [Fact]
    public async Task Put_derives_the_stored_esv_and_limit_from_the_minimum_wage()
    {
        const int year = 2031;
        const long minWageKop = 864_777L;
        const int esvRateBp = 2233;
        const int incomeLimitMinWages = 1200;

        using var client = await SignIn();

        var response = await client.PutAsJsonAsync(
            $"/api/tax-years/{year}",
            Request(minWageKop: minWageKop, esvRateBp: esvRateBp, incomeLimitMinWages: incomeLimitMinWages));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.Content.ReadFromJsonAsync<TaxYearConfigResponse>();
        Assert.Equal(Money.ApplyBp(minWageKop, esvRateBp), config!.EsvMonthlyKop);
        Assert.Equal(minWageKop * incomeLimitMinWages, config.IncomeLimitKop);

        var reread = await client.GetFromJsonAsync<TaxYearConfigResponse>($"/api/tax-years/{year}");
        Assert.Equal(config.EsvMonthlyKop, reread!.EsvMonthlyKop);
        Assert.Equal(config.IncomeLimitKop, reread.IncomeLimitKop);
    }

    [Fact]
    public async Task Put_ignores_a_body_that_dictates_the_derived_fields()
    {
        const int year = 2032;
        const long minWageKop = 864_777L;
        const int esvRateBp = 2233;
        const int incomeLimitMinWages = 1200;

        using var client = await SignIn();
        var body = $$"""
            {
              "minWageKop": {{minWageKop}},
              "singleTaxRateBp": 600,
              "militaryLevyRateBp": 200,
              "esvRateBp": {{esvRateBp}},
              "excessRateBp": 1600,
              "incomeLimitMinWages": {{incomeLimitMinWages}},
              "limitWarnThresholdsPct": [80, 95],
              "esvDeadlineDay": 20,
              "declarationDays": 41,
              "taxPaymentDaysAfterDeclaration": 11,
              "advanceRecommendedDay": 16,
              "holidays": [],
              "source": "a test body",
              "esvMonthlyKop": 1,
              "incomeLimitKop": 1,
              "verifiedAt": "2026-01-01T00:00:00+00:00",
              "year": 1999
            }
            """;

        var response = await client.PutAsync(
            $"/api/tax-years/{year}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.Content.ReadFromJsonAsync<TaxYearConfigResponse>();
        Assert.Equal(year, config!.Year);
        Assert.Null(config.VerifiedAt);
        Assert.Equal(Money.ApplyBp(minWageKop, esvRateBp), config.EsvMonthlyKop);
        Assert.Equal(minWageKop * incomeLimitMinWages, config.IncomeLimitKop);
    }

    [Theory]
    [InlineData(0, 2033, HttpStatusCode.BadRequest)]
    [InlineData(28, 2034, HttpStatusCode.OK)]
    [InlineData(29, 2035, HttpStatusCode.BadRequest)]
    [InlineData(31, 2036, HttpStatusCode.BadRequest)]
    public async Task Put_accepts_an_esv_deadline_day_every_month_has(
        int esvDeadlineDay,
        int year,
        HttpStatusCode expected)
    {
        using var client = await SignIn();

        var response = await client.PutAsJsonAsync(
            $"/api/tax-years/{year}",
            Request(esvDeadlineDay: esvDeadlineDay));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Contains(
                nameof(TaxYearConfigRequest.EsvDeadlineDay),
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Put_rejects_an_advance_day_the_following_month_may_not_have()
    {
        using var client = await SignIn();

        var response = await client.PutAsJsonAsync(
            "/api/tax-years/2037",
            Request(advanceRecommendedDay: 31));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            nameof(TaxYearConfigRequest.AdvanceRecommendedDay),
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    // Pins the serializer options Program.cs sets: without them a body missing a member reaches the
    // handler with null in a non-nullable property and stores a row with no source.
    [Fact]
    public async Task Put_rejects_a_body_that_omits_a_member()
    {
        using var client = await SignIn();
        var body = """
            {
              "minWageKop": 800000,
              "singleTaxRateBp": 600,
              "militaryLevyRateBp": 200,
              "esvRateBp": 2100,
              "excessRateBp": 1600,
              "incomeLimitMinWages": 1200,
              "limitWarnThresholdsPct": [80, 95],
              "esvDeadlineDay": 20,
              "declarationDays": 41,
              "taxPaymentDaysAfterDeclaration": 11,
              "advanceRecommendedDay": 16,
              "holidays": []
            }
            """;

        var response = await client.PutAsync(
            "/api/tax-years/2043",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tax-years/2043")).StatusCode);
    }

    [Fact]
    public async Task Clone_copies_every_value_into_an_unverified_next_year()
    {
        using var client = await SignIn();
        var source = await client.GetFromJsonAsync<TaxYearConfigResponse>("/api/tax-years/2026");

        var created = await client.PostAsync("/api/tax-years/2026/clone-to/2027", content: null);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var clone = await created.Content.ReadFromJsonAsync<TaxYearConfigResponse>();
        Assert.Null(clone!.VerifiedAt);
        Assert.Equal(
            JsonSerializer.Serialize(source! with { Year = 2027 }),
            JsonSerializer.Serialize(clone));

        var again = await client.PostAsync("/api/tax-years/2026/clone-to/2027", content: null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Clone_from_a_year_without_a_row_is_not_found()
    {
        using var client = await SignIn();

        var response = await client.PostAsync("/api/tax-years/2098/clone-to/2099", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Verify_stamps_the_row_and_the_next_write_clears_the_stamp()
    {
        const int year = 2038;
        using var client = await SignIn();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", Request())).StatusCode);

        var verified = await client.PostAsync($"/api/tax-years/{year}/verify", content: null);

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var stamped = await verified.Content.ReadFromJsonAsync<TaxYearConfigResponse>();
        Assert.NotNull(stamped!.VerifiedAt);

        var rewritten = await client.PutAsJsonAsync($"/api/tax-years/{year}", Request());

        Assert.Equal(HttpStatusCode.OK, rewritten.StatusCode);
        var cleared = await rewritten.Content.ReadFromJsonAsync<TaxYearConfigResponse>();
        Assert.Null(cleared!.VerifiedAt);
    }

    [Fact]
    public async Task Verify_a_year_without_a_row_is_not_found()
    {
        using var client = await SignIn();

        var response = await client.PostAsync("/api/tax-years/2039/verify", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_year_without_a_row_is_not_found()
    {
        using var client = await SignIn();

        var response = await client.GetAsync("/api/tax-years/2040");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/tax-years")]
    [InlineData("GET", "/api/tax-years/2041")]
    [InlineData("PUT", "/api/tax-years/2041")]
    [InlineData("POST", "/api/tax-years/2041/verify")]
    [InlineData("POST", "/api/tax-years/2041/clone-to/2042")]
    public async Task Every_route_without_a_session_is_unauthorized(string method, string path)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(Request());
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Deliberately unlike the seeded 2026 values, so a test that asserts a stored number cannot pass
    // on a handler that quietly kept the row it found.
    private static TaxYearConfigRequest Request(
        long minWageKop = 800_000L,
        int singleTaxRateBp = 600,
        int militaryLevyRateBp = 200,
        int esvRateBp = 2100,
        int excessRateBp = 1600,
        int incomeLimitMinWages = 1200,
        int esvDeadlineDay = 20,
        int declarationDays = 41,
        int taxPaymentDaysAfterDeclaration = 11,
        int advanceRecommendedDay = 16) =>
        new(
            minWageKop,
            singleTaxRateBp,
            militaryLevyRateBp,
            esvRateBp,
            excessRateBp,
            incomeLimitMinWages,
            [80, 95],
            esvDeadlineDay,
            declarationDays,
            taxPaymentDaysAfterDeclaration,
            advanceRecommendedDay,
            [],
            "a test source");

    private async Task<HttpClient> SignIn()
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={ApiFixture.AllowedEmail}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return client;
    }
}
