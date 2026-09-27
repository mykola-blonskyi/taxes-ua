using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Payments;

// ApiFixture is an IClassFixture, so this class owns its database. xUnit fixes no order between the
// tests, so each one works in a year of its own.
public sealed class PaymentsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_payment_is_created_listed_edited_and_deleted()
    {
        const int year = 2070;
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var created = await Post(client, Body(year, PaymentKind.Esv, 190_234, quarter: 1, note: "  April ESV  "));
        Assert.Equal(
            (PaymentKind.Esv, 190_234L, year, (int?)1, (int?)null, "April ESV"),
            (created.Kind, created.AmountKop, created.PeriodYear, created.PeriodQuarter, created.PeriodMonth, created.Note));

        var listed = await List(client, year);
        Assert.Equal(created, Assert.Single(listed.Items));

        var edit = Body(year, PaymentKind.SingleTax, 50_000, month: 5);
        var updated = await client.PutAsJsonAsync($"/api/payments/{created.Id}", edit, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var item = Assert.Single((await List(client, year)).Items);
        Assert.Equal(
            (PaymentKind.SingleTax, 50_000L, (int?)null, (int?)5, (string?)null),
            (item.Kind, item.AmountKop, item.PeriodQuarter, item.PeriodMonth, item.Note));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/payments/{created.Id}")).StatusCode);
        Assert.Empty((await List(client, year)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/payments/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_payment_is_listed_under_the_year_of_its_period_not_the_day_it_was_paid()
    {
        const int year = 2071;
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        await Post(client, Body(year, PaymentKind.MilitaryLevy, 10_000, quarter: 4, paidOn: new DateOnly(year + 1, 2, 10)));

        Assert.Single((await List(client, year)).Items);
        Assert.Empty((await List(client, year + 1)).Items);
    }

    [Theory]
    [InlineData(null, null, "periodQuarter")]
    [InlineData(1, 1, "periodQuarter")]
    [InlineData(0, null, "periodQuarter")]
    [InlineData(5, null, "periodQuarter")]
    [InlineData(null, 0, "periodMonth")]
    [InlineData(null, 13, "periodMonth")]
    public async Task A_period_that_is_not_one_quarter_or_one_month_is_rejected(int? quarter, int? month, string key)
    {
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsJsonAsync(
            "/api/payments", Body(2072, PaymentKind.Esv, 1_000, quarter, month), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000_000_000_001)]
    public async Task An_amount_outside_the_allowed_range_is_rejected(long amountKop)
    {
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsJsonAsync(
            "/api/payments", Body(2072, PaymentKind.Esv, amountKop, quarter: 1), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "amountKop");
    }

    [Fact]
    public async Task A_period_year_outside_the_supported_range_is_rejected()
    {
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsJsonAsync(
            "/api/payments", Body(1999, PaymentKind.Esv, 1_000, quarter: 1, paidOn: new DateOnly(2026, 1, 1)), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "periodYear");
    }

    [Fact]
    public async Task One_owner_can_neither_see_nor_change_another_owners_payment()
    {
        const int year = 2073;
        using var owner = await SignIn(fixture, ApiFixture.AllowedEmail);
        using var other = await SignIn(fixture, ApiFixture.SecondAllowedEmail);
        var payment = await Post(owner, Body(year, PaymentKind.Esv, 70_000, quarter: 2));

        Assert.Empty((await List(other, year)).Items);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/payments/{payment.Id}", Body(year, PaymentKind.Esv, 1, quarter: 2), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/payments/{payment.Id}")).StatusCode);

        Assert.Equal(payment, Assert.Single((await List(owner, year)).Items));
    }

    [Fact]
    public async Task Without_a_session_the_routes_are_unauthorized()
    {
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/payments?year=2026")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/payments", Body(2026, PaymentKind.Esv, 1, quarter: 1), Json)).StatusCode);
    }

    // Registration on 2080-01-01 and one Q1 receipt of 100,000.00: the single tax is 6,000.00, the levy
    // 2,000.00, and ESV 1,680.00 a month. On 2080-06-30 every Q1 deadline has passed and Q2's have not.
    [Fact]
    public async Task The_periods_obligations_match_the_engine_and_keep_the_kinds_apart()
    {
        const int year = 2080;
        var today = new DateOnly(year, 6, 30);
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), today);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(), Json)).StatusCode);
        await SetRegistrationDate(owner, new DateOnly(year, 1, 1));
        await SetRegistrationDate(other, new DateOnly(year, 1, 1));
        await PostIncome(owner, new DateOnly(year, 2, 1), 10_000_000);

        await Post(owner, Body(year, PaymentKind.Esv, 1_008_000, quarter: 1));
        await Post(owner, Body(year, PaymentKind.SingleTax, 1_000_000, quarter: 1));
        await Post(owner, Body(year, PaymentKind.Esv, 168_000, month: 7));
        await Post(other, Body(year, PaymentKind.MilitaryLevy, 200_000, quarter: 1));

        var periods = await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

        var expected = await EngineObligations(application, ApiFixture.AllowedEmail, year, today);
        Assert.Equal(12, expected.Count);
        foreach (var obligation in expected)
        {
            var quarter = Assert.Single(periods!.Quarters, q => q.Quarter == obligation.Quarter);
            var obligations = quarter.Obligations!;
            var actual = obligation.Kind switch
            {
                PaymentKind.SingleTax => obligations.SingleTax,
                PaymentKind.MilitaryLevy => obligations.MilitaryLevy,
                _ => obligations.Esv,
            };
            Assert.Equal(
                new ObligationResponse(
                    obligation.AccruedKop,
                    obligation.PaidKop,
                    obligation.OpeningBalanceKop,
                    obligation.BalanceKop,
                    obligation.DueDate,
                    obligation.Status),
                actual);
        }

        var q1 = periods!.Quarters[0].Obligations!;
        var q2 = periods.Quarters[1].Obligations!;
        var q3 = periods.Quarters[2].Obligations!;

        Assert.Equal((-504_000L, ObligationStatus.Done), (q1.Esv.BalanceKop, q1.Esv.Status));
        Assert.Equal((-504_000L, 0L, ObligationStatus.Done), (q2.Esv.OpeningBalanceKop, q2.Esv.BalanceKop, q2.Esv.Status));
        Assert.Equal((168_000L, 336_000L, ObligationStatus.Upcoming), (q3.Esv.PaidKop, q3.Esv.BalanceKop, q3.Esv.Status));

        Assert.Equal((-400_000L, ObligationStatus.Done), (q1.SingleTax.BalanceKop, q1.SingleTax.Status));
        Assert.Equal((200_000L, ObligationStatus.Overdue), (q1.MilitaryLevy.BalanceKop, q1.MilitaryLevy.Status));
        Assert.Equal((200_000L, ObligationStatus.Upcoming), (q2.MilitaryLevy.BalanceKop, q2.MilitaryLevy.Status));

        Assert.NotNull(periods.Balances);
        Assert.Equal(new KindYearBalance(600_000, 1_000_000, -400_000), periods.Balances.SingleTax);
        Assert.Equal(new KindYearBalance(200_000, 0, 200_000), periods.Balances.MilitaryLevy);
        Assert.Equal(new KindYearBalance(2_016_000, 1_176_000, 840_000), periods.Balances.Esv);
    }

    [Fact]
    public async Task Without_a_registration_date_there_are_no_obligations_and_no_balances()
    {
        const int year = 2081;
        using var client = await SignIn(fixture, ApiFixture.SecondAllowedEmail);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(), Json)).StatusCode);
        await SetRegistrationDate(client, null);
        await Post(client, Body(year, PaymentKind.Esv, 5_000, quarter: 3));

        var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

        Assert.All(periods!.Quarters, quarter => Assert.Null(quarter.Obligations));
        Assert.Null(periods.Balances);
        Assert.Single((await List(client, year)).Items);
    }

    private static async Task<IReadOnlyList<Obligation>> EngineObligations(
        WebApplicationFactory<Program> application, string email, int year, DateOnly today)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = database.Users.Single(user => user.Email == email).Id;
        var loaded = (await YearAccruals.LoadAsync(database, userId, year, CancellationToken.None))!;
        var payments = await PaymentsEndpoints.LoadEngineInputAsync(database, userId, year, CancellationToken.None);

        return ObligationBuilder.ForYear(
            Balances.ForYear(loaded.Accrual, payments),
            loaded.Config.ToEngineInput(),
            loaded.Settings.ToEngineInput(),
            today);
    }

    private static PaymentRequest Body(
        int year,
        PaymentKind kind,
        long amountKop,
        int? quarter = null,
        int? month = null,
        string? note = null,
        DateOnly? paidOn = null) =>
        new(paidOn ?? new DateOnly(year, 4, 15), kind, amountKop, year, quarter, month, note);

    private static async Task<PaymentResponse> Post(HttpClient client, PaymentRequest body)
    {
        var response = await client.PostAsJsonAsync("/api/payments", body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
    }

    private static async Task<PaymentListResponse> List(HttpClient client, int year) =>
        (await client.GetFromJsonAsync<PaymentListResponse>($"/api/payments?year={year}", Json))!;

    private static async Task PostIncome(HttpClient client, DateOnly valueDate, long amountKop)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            new TransactionRequest(
                valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task SetRegistrationDate(HttpClient client, DateOnly? date)
    {
        var request = new SettingsRequest(
            FopRegistrationDate: date,
            PaymentMode: PaymentMode.Quarterly,
            EsvRegistrationMonthPolicy: Api.Features.Settings.EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
            Locale: "uk",
            Theme: "system",
            DefaultCurrency: "UAH");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);
    }

    private static TaxYearConfigRequest TaxYearRequest() => new(
        800_000L, 600, 200, 2100, 1600, 1200, [80, 95], 20, 41, 11, 16, [], "a test source");

    private static async Task AssertErrorKey(HttpResponseMessage response, string key)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(key, out _), $"no error under {key}");
    }

    private static async Task<HttpClient> SignIn(ApiFixture fixture, string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(login.Headers.Location)).StatusCode);
        return client;
    }
}
