using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Dashboard;

// ApiFixture is an IClassFixture, so this class owns its database. xUnit runs one class's tests in
// sequence but in no fixed order, so each test sets the registration date it needs, and only the
// second owner ever records income or payments. The clock is set in 2080 because the test client
// drops a session cookie whose expiry is already past on the real clock; registering in 2080 leaves
// the seeded 2026 with nothing accrued.
public sealed class DashboardEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const int Year = 2080;

    private const long EsvQuarterKop = 570_702;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Q1's ESV falls due on 2080-04-19, and Kyiv is UTC+3 in April: 21:00 UTC on the 18th is already
    // the 19th in Kyiv.
    [Theory]
    [InlineData("2080-04-18T20:59:59Z", "2080-04-18", ObligationStatus.Upcoming, 1)]
    [InlineData("2080-04-18T21:00:00Z", "2080-04-19", ObligationStatus.Due, 0)]
    [InlineData("2080-04-19T20:59:59Z", "2080-04-19", ObligationStatus.Due, 0)]
    [InlineData("2080-04-19T21:00:00Z", "2080-04-20", ObligationStatus.Overdue, -1)]
    public async Task Today_is_the_kyiv_day_not_the_utc_day(
        string utcNow, string kyivToday, ObligationStatus status, int daysLeft)
    {
        await using var application = At(DateTimeOffset.Parse(utcNow));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, new DateOnly(Year, 1, 1));

        var dashboard = await Get(client);

        Assert.Equal(DateOnly.Parse(kyivToday), dashboard.Today);
        Assert.Equal(NextStepState.Pay, dashboard.NextStep.State);
        Assert.Equal(
            new KindDebtResponse(PaymentKind.Esv, Year, 1, Year, 1, EsvQuarterKop, new DateOnly(Year, 4, 19), status, daysLeft, null),
            Assert.Single(dashboard.NextStep.Now));
    }

    [Fact]
    public async Task A_payment_moves_the_step_to_the_next_deadline()
    {
        await using var application = At(new DateTimeOffset(Year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, new DateOnly(Year, 1, 1));
        await PostIncome(client, new DateOnly(Year, 2, 10), 1_000_000);

        var before = await Get(client);

        Assert.Equal(
            [new KindDebtResponse(PaymentKind.Esv, Year, 1, Year, 1, EsvQuarterKop, new DateOnly(Year, 4, 19), ObligationStatus.Overdue, -12, null)],
            before.NextStep.Now);
        Assert.Equal(
            [(PaymentKind.SingleTax, 50_000L, 19), (PaymentKind.MilitaryLevy, 10_000L, 19)],
            before.NextStep.Later.Select(debt => (debt.Kind, debt.AmountKop, debt.DaysLeft)));
        Assert.Equal(new TaxBurdenResponse(1_000_000, 60_000 + 2 * EsvQuarterKop, 12_014), before.Burden);

        var payment = await client.PostAsJsonAsync(
            "/api/payments",
            new PaymentRequest(new DateOnly(Year, 5, 1), PaymentKind.Esv, EsvQuarterKop, Year, 1, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);

        var after = await Get(client);

        Assert.Equal(
            [(PaymentKind.SingleTax, 50_000L), (PaymentKind.MilitaryLevy, 10_000L)],
            after.NextStep.Now.Select(debt => (debt.Kind, debt.AmountKop)));
        Assert.Equal(
            [new KindDebtResponse(PaymentKind.Esv, Year, 2, Year, 2, EsvQuarterKop, new DateOnly(Year, 7, 19), ObligationStatus.Upcoming, 79, null)],
            after.NextStep.Later);
        Assert.Empty(after.Credits);

        await PostPayment(client, PaymentKind.SingleTax, 60_000);
        await PostPayment(client, PaymentKind.MilitaryLevy, 10_000);

        var overpaid = await Get(client);

        Assert.Equal([new KindCreditResponse(PaymentKind.SingleTax, 10_000)], overpaid.Credits);
        Assert.DoesNotContain(overpaid.NextStep.Later, debt => debt.Kind == PaymentKind.SingleTax);
    }

    [Fact]
    public async Task A_gap_in_the_configured_years_says_which_year_is_missing()
    {
        await using var application = At(new DateTimeOffset(Year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, new DateOnly(Year - 1, 1, 1));

        var dashboard = await Get(client);

        Assert.Equal(
            (NextStepState.MissingTaxYear, (int?)(Year - 1)),
            (dashboard.NextStep.State, dashboard.NextStep.MissingTaxYear));
        Assert.Null(dashboard.Burden);
    }

    [Fact]
    public async Task Without_a_registration_date_there_is_no_step_and_no_burden()
    {
        await using var application = At(new DateTimeOffset(Year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, null);

        var dashboard = await Get(client);

        Assert.Equal(NextStepState.RegistrationDateNotSet, dashboard.NextStep.State);
        Assert.Empty(dashboard.NextStep.Now);
        Assert.Null(dashboard.Burden);
    }

    [Fact]
    public async Task Before_the_registration_date_the_step_names_it()
    {
        await using var application = At(new DateTimeOffset(Year, 9, 27, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, new DateOnly(Year, 10, 1));

        var dashboard = await Get(client);

        Assert.Equal(
            (NextStepState.BeforeRegistration, (DateOnly?)new DateOnly(Year, 10, 1)),
            (dashboard.NextStep.State, dashboard.NextStep.RegistrationDate));
        Assert.Null(dashboard.Burden);
    }

    [Fact]
    public async Task Without_a_session_the_route_is_unauthorized()
    {
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    private WebApplicationFactory<Program> At(DateTimeOffset utcNow) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(utcNow))));

    private static async Task PostPayment(HttpClient client, PaymentKind kind, long amountKop)
    {
        var response = await client.PostAsJsonAsync(
            "/api/payments", new PaymentRequest(new DateOnly(Year, 5, 1), kind, amountKop, Year, 1, null, null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<DashboardResponse> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static async Task PostIncome(HttpClient client, DateOnly valueDate, long amountKop)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            new TransactionRequest(
                valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The 2026 parameters, so ESV is 1,902.34 a month.
    private static async Task SetUp(HttpClient client, DateOnly? registrationDate)
    {
        var year = new TaxYearConfigRequest(
            864_700, 500, 100, 2_200, 1_500, 1_167, [85, 100], 19, 40, 10, 15, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tax-years/{Year}", year, Json)).StatusCode);

        var request = new SettingsRequest(
            FopRegistrationDate: registrationDate,
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
}
