using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Periods;

// Each test signs in as its own owner, so the two never see each other's receipts or payments. The
// clock is in 2080 for the reason DashboardEndpointsTests gives.
public sealed class MonthlyAdvancesEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const int Year = 2080;

    private const long EsvMonthKop = 190_234;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Switching_to_advances_adds_the_months_and_moves_the_step_but_changes_no_figure()
    {
        await using var application = At(new DateTimeOffset(Year, 8, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, PaymentMode.Quarterly);

        var quarterlyPeriods = await GetPeriods(client);
        var quarterlyStep = (await GetDashboard(client)).NextStep;

        Assert.Null(quarterlyPeriods["months"]);
        Assert.Equal(
            // 2080-10-19 is a Saturday.
            [(PaymentKind.Esv, 3 * EsvMonthKop, new DateOnly(Year, 10, 21), (int?)null)],
            quarterlyStep.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)));

        await PutSettings(client, PaymentMode.MonthlyAdvance);
        var advancePeriods = await GetPeriods(client);
        var advanceStep = (await GetDashboard(client)).NextStep;

        Assert.Equal(WithoutMonths(quarterlyPeriods), WithoutMonths(advancePeriods));
        var months = advancePeriods["months"].Deserialize<MonthPeriodResponse[]>(Json)!;
        Assert.Equal(Enumerable.Range(1, 12), months.Select(month => month.Month));
        Assert.Equal(
            new MonthPeriodResponse(7, 1_000_000, 50_000, 10_000, EsvMonthKop, 60_000 + EsvMonthKop, new DateOnly(Year, 8, 15)),
            months[6]);
        Assert.Equal(0, months[0].RecommendedKop);
        Assert.Equal(
            [
                (PaymentKind.SingleTax, 50_000L, new DateOnly(Year, 8, 15), (int?)7),
                (PaymentKind.MilitaryLevy, 10_000L, new DateOnly(Year, 8, 15), (int?)7),
                (PaymentKind.Esv, EsvMonthKop, new DateOnly(Year, 8, 15), (int?)7),
            ],
            advanceStep.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.DueDate, debt.AdvanceMonth)).OrderBy(debt => debt.Kind));
    }

    [Fact]
    public async Task A_payment_named_for_a_month_is_credited_to_that_months_quarter()
    {
        await using var application = At(new DateTimeOffset(Year, 8, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, PaymentMode.MonthlyAdvance);

        await PostPayment(client, PaymentKind.Esv, EsvMonthKop, periodMonth: 7);

        var periods = (await GetPeriods(client)).Deserialize<PeriodsResponse>(Json)!;
        var q3 = periods.Quarters.Single(quarter => quarter.Quarter == 3).Obligations!.Esv;
        Assert.Equal((EsvMonthKop, 2 * EsvMonthKop), (q3.PaidKop, q3.RemainingKop));
        Assert.Equal(60_000, periods.Months![6].RecommendedKop);
        Assert.Equal(EsvMonthKop, periods.Months[7].RecommendedKop);
    }

    private WebApplicationFactory<Program> At(DateTimeOffset utcNow) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(utcNow))));

    private static string WithoutMonths(JsonObject periods)
    {
        var copy = periods.DeepClone().AsObject();
        copy.Remove("months");
        return copy.ToJsonString();
    }

    private static async Task<JsonObject> GetPeriods(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonObject>($"/api/periods/{Year}", Json))!;

    private static async Task<DashboardResponse> GetDashboard(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static async Task PostPayment(
        HttpClient client, PaymentKind kind, long amountKop, int? periodQuarter = null, int? periodMonth = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/payments",
            new PaymentRequest(new DateOnly(Year, 7, 20), kind, amountKop, Year, periodQuarter, periodMonth, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The 2026 parameters, registered on New Year's Day, the first half-year's ESV paid and one July
    // receipt, so the third quarter is the open one.
    private static async Task SetUp(HttpClient client, PaymentMode mode)
    {
        var year = new TaxYearConfigRequest(
            864_700, 500, 100, 2_200, 1_500, 1_167, [85, 100], 19, 40, 10, 15, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tax-years/{Year}", year, Json)).StatusCode);
        await PutSettings(client, mode);

        var receipt = await client.PostAsJsonAsync(
            "/api/transactions",
            new TransactionRequest(
                new DateOnly(Year, 7, 10), 1_000_000, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        await PostPayment(client, PaymentKind.Esv, 6 * EsvMonthKop, periodQuarter: 1);
    }

    private static async Task PutSettings(HttpClient client, PaymentMode mode)
    {
        var request = new SettingsRequest(
            FopRegistrationDate: new DateOnly(Year, 1, 1),
            PaymentMode: mode,
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
