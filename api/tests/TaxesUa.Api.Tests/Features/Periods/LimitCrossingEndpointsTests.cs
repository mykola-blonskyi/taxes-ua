using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Tests.Features.Periods;

// ApiFixture is an IClassFixture, so this class owns its database, and each test uses a year of its
// own. The limit is one minimum wage (8,647.00 UAH), so 5,000.00 in Q1 and 5,000.00 in Q2 cross it in
// Q2 by 1,353.00, taxed 202.95 at 15%, and 3,000.00 in Q3 falls after group 3 ended.
public sealed class LimitCrossingEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const long EsvQuarterKop = 570_702;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Periods_stop_at_the_crossing_quarter_whose_single_tax_includes_the_excess()
    {
        const int year = 2083;
        await using var application = At(new DateOnly(year, 8, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUpCrossedInQ2(owner, year);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json))!;

        Assert.Equal(new LimitCrossingResponse(year, 2, year, 3, null), periods.LimitCrossing);
        Assert.Equal([1, 2], periods.Quarters.Select(quarter => quarter.Quarter));
        var q2 = periods.Quarters[1];
        Assert.Equal(
            (38_530L, 63_530L, 135_300L, 20_295L, 10_000L),
            (q2.SingleTaxKop, q2.CumulativeSingleTaxKop, q2.CumulativeExcessIncomeKop, q2.CumulativeExcessTaxKop,
                q2.CumulativeMilitaryLevyKop));
        Assert.Equal(38_530, q2.Obligations!.SingleTax.AccruedKop);
        Assert.Equal((63_530L, 2 * EsvQuarterKop), (periods.Balances!.SingleTax.OwedKop, periods.Balances.Esv.OwedKop));
    }

    [Fact]
    public async Task The_home_screen_owes_the_excess_and_warns_instead_of_computing_the_later_quarter()
    {
        const int year = 2084;
        await using var application = At(new DateOnly(year, 8, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUpCrossedInQ2(owner, year);

        var dashboard = (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

        Assert.Equal(new LimitCrossingResponse(year, 2, year, 3, null), dashboard.LimitCrossing);
        Assert.Equal(NextStepState.Pay, dashboard.NextStep.State);
        Assert.Equal(
            [(PaymentKind.SingleTax, 63_530L, 2), (PaymentKind.MilitaryLevy, 10_000L, 2), (PaymentKind.Esv, 2 * EsvQuarterKop, 2)],
            dashboard.NextStep.Now.Select(debt => (debt.Kind, debt.AmountKop, debt.ToQuarter)).OrderBy(debt => debt.Kind));
        Assert.Empty(dashboard.NextStep.Later);
        Assert.Null(dashboard.Burden);
        Assert.Equal(63_530 + 10_000 + (2 * EsvQuarterKop), dashboard.Reserve!.TotalKop);
        Assert.Equal((1_000_000L, 135_300L, 20_295L), (dashboard.Limit!.IncomeKop, dashboard.Limit.ExcessKop, dashboard.Limit.ExcessTaxKop));
    }

    [Fact]
    public async Task A_later_quarter_has_no_figures_and_its_declaration_is_blocked()
    {
        const int year = 2085;
        await using var application = At(new DateOnly(year, 10, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUpCrossedInQ2(owner, year);

        var crossing = await Declaration(owner, year, 2);
        var after = await Declaration(owner, year, 3);

        Assert.Equal((false, 135_300L, 63_530L), (crossing.Readiness.OutsideGroup3, crossing.Figures!.ExcessIncomeKop, crossing.Figures.TotalSingleTaxKop));
        Assert.Equal((true, false), (after.Readiness.OutsideGroup3, after.Readiness.Ready));
        Assert.Null(after.Figures);
        Assert.Equal(new LimitCrossingResponse(year, 2, year, 3, null), after.LimitCrossing);
    }

    private WebApplicationFactory<Program> At(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero)))));

    private static async Task SetUpCrossedInQ2(HttpClient owner, int year)
    {
        var taxYear = new TaxYearConfigRequest(
            864_700, 500, 100, 2_200, 1_500, 1, [85, 100], 19, 40, 10, 15, 10, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", taxYear, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/tax-years/{year}/verify", null)).StatusCode);

        var settings = new SettingsRequest(
            new DateOnly(year, 1, 1),
            PaymentMode.Quarterly,
            EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday],
            "uk",
            "system",
            "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", settings, Json)).StatusCode);

        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 8, 10), 300_000);
    }

    private static async Task PostIncome(HttpClient owner, DateOnly valueDate, long amountKop)
    {
        var request = new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", request, Json)).StatusCode);
    }

    private static async Task<DeclarationResponse> Declaration(HttpClient owner, int year, int quarter)
    {
        var response = await owner.GetAsync($"/api/declarations/{year}/{quarter}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationResponse>(Json))!;
    }
}
