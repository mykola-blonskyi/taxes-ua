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
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;
using KindYearBalance = TaxesUa.Api.Features.Periods.KindYearBalance;

namespace TaxesUa.Api.Tests.Features.Periods;

// ApiFixture is an IClassFixture, so this class owns its database. Each test registers on 1 January of
// a year of its own and configures that year and the next, with an unconfigured year between tests so
// no test's ledger runs into another's. The limit is one minimum wage (8,647.00 UAH).
public sealed class LimitCrossingNextYearEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task After_a_q4_crossing_the_next_year_warns_and_computes_no_group_3_obligations()
    {
        const int year = 2091;
        await using var application = At(new DateOnly(year + 1, 5, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 11, 10), 1_000_000);
        await PostIncome(owner, new DateOnly(year + 1, 2, 10), 300_000);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!;
        var dashboard = (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;
        var declaration = await Declaration(owner, year + 1, 1);

        Assert.Equal((year, 4, year + 1, 1), Of(periods.LimitCrossing));
        Assert.Empty(periods.Quarters);
        Assert.Equal((0L, 0L, 0L), (periods.Balances!.SingleTax.AccruedKop, periods.Balances.MilitaryLevy.AccruedKop, periods.Balances.Esv.AccruedKop));

        Assert.Equal((year, 4, year + 1, 1), Of(dashboard.LimitCrossing));
        Assert.All(dashboard.NextStep.Now.Concat(dashboard.NextStep.Later), debt => Assert.Equal((year, 4), (debt.ToYear, debt.ToQuarter)));
        Assert.All(dashboard.Reserve!.Dues, due => Assert.True(due.DueDate < new DateOnly(year + 1, 3, 1), due.ToString()));
        Assert.Null(dashboard.Burden);
        Assert.Null(dashboard.Limit);

        Assert.Null(declaration.Figures);
        Assert.Equal((true, false), (declaration.Readiness.OutsideGroup3, declaration.Readiness.Ready));
        Assert.Equal((year, 4, year + 1, 1), Of(declaration.LimitCrossing));
    }

    [Fact]
    public async Task After_a_mid_year_crossing_the_next_year_is_outside_group_3_too()
    {
        const int year = 2094;
        await using var application = At(new DateOnly(year + 1, 8, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);
        await PostIncome(owner, new DateOnly(year + 1, 2, 10), 300_000);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!;
        var declaration = await Declaration(owner, year + 1, 2);

        Assert.Equal((year, 2, year, 3), Of(periods.LimitCrossing));
        Assert.Empty(periods.Quarters);
        Assert.Null(declaration.Figures);
        Assert.True(declaration.Readiness.OutsideGroup3);
    }

    [Fact]
    public async Task Back_on_group_3_resumes_computation_from_its_quarter_only()
    {
        const int year = 2097;
        await using var application = At(new DateOnly(year + 1, 11, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new YearQuarter(year + 1, 2));
        await PostIncome(owner, new DateOnly(year, 11, 10), 1_000_000);
        await PostIncome(owner, new DateOnly(year + 1, 2, 10), 300_000);
        await PostIncome(owner, new DateOnly(year + 1, 5, 10), 200_000);
        await PostIncome(owner, new DateOnly(year + 1, 8, 10), 100_000);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!;
        var q1 = await Declaration(owner, year + 1, 1);
        var q2 = await Declaration(owner, year + 1, 2);
        var q3 = await Declaration(owner, year + 1, 3);

        Assert.Equal(new YearQuarter(year + 1, 2), periods.LimitCrossing!.BackOnGroup3From);
        Assert.Equal([2, 3, 4], periods.Quarters.Select(quarter => quarter.Quarter));
        Assert.Equal((200_000L, 10_000L), (periods.Quarters[0].CumulativeIncomeKop, periods.Quarters[0].SingleTaxKop));
        Assert.NotNull(periods.Quarters[0].Obligations);
        Assert.Null(q1.Figures);
        Assert.Equal((200_000L, 0L, 10_000L), (q2.Figures!.TotalIncomeKop, q2.Figures.PreviousSingleTaxKop, q2.Figures.SingleTaxPayableKop));
        Assert.Equal((300_000L, 10_000L, 5_000L), (q3.Figures!.TotalIncomeKop, q3.Figures.PreviousSingleTaxKop, q3.Figures.SingleTaxPayableKop));
    }

    [Fact]
    public async Task A_payment_naming_a_quarter_outside_group_3_is_listed_apart_from_the_balances()
    {
        const int year = 2088;
        await using var application = At(new DateOnly(year + 1, 11, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new YearQuarter(year + 1, 2));
        await PostIncome(owner, new DateOnly(year, 11, 10), 1_000_000);
        await PostEsv(owner, year + 1, periodQuarter: 1, periodMonth: null, 570_702);
        await PostEsv(owner, year + 1, periodQuarter: null, periodMonth: 3, 190_234);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!;

        Assert.Equal(
            [
                new OutsideGroup3PaymentResponse(PaymentKind.Esv, 1, null, 570_702),
                new OutsideGroup3PaymentResponse(PaymentKind.Esv, 1, 3, 190_234),
            ],
            periods.Balances!.OutsideGroup3Payments.OrderBy(payment => payment.Month ?? 0));
        Assert.Equal(new KindYearBalance(4 * 570_702, 3 * 570_702, 0, 7 * 570_702, 0), periods.Balances.Esv);
        Assert.Equal(ObligationStatus.Overdue, periods.Quarters[0].Obligations!.Esv.Status);
    }

    [Fact]
    public async Task Moving_back_on_group_3_moves_a_payment_into_the_ledger()
    {
        const int year = 2082;
        await using var application = At(new DateOnly(year + 1, 11, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new YearQuarter(year + 1, 2));
        await PostIncome(owner, new DateOnly(year, 11, 10), 1_000_000);
        await PostEsv(owner, year + 1, periodQuarter: 1, periodMonth: null, 570_702);

        var stopped = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!.Balances!;
        var moved = await owner.PutAsJsonAsync(
            "/api/settings", Settings(new DateOnly(year, 1, 1), new YearQuarter(year + 1, 1)), Json);
        var resumed = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!.Balances!;

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal([new OutsideGroup3PaymentResponse(PaymentKind.Esv, 1, null, 570_702)], stopped.OutsideGroup3Payments);
        Assert.Equal(4 * 570_702, stopped.Esv.EarlierOwedKop);
        Assert.Empty(resumed.OutsideGroup3Payments);
        Assert.Equal(3 * 570_702, resumed.Esv.EarlierOwedKop);
    }

    [Fact]
    public async Task After_a_return_and_a_second_crossing_the_group_3_quarters_name_what_the_ledger_counts()
    {
        const int year = 2079;
        await using var application = At(new DateOnly(year + 1, 11, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new YearQuarter(year + 1, 2));
        await PostIncome(owner, new DateOnly(year, 11, 10), 1_000_000);
        await PostIncome(owner, new DateOnly(year + 1, 5, 10), 200_000);
        await PostIncome(owner, new DateOnly(year + 1, 8, 10), 800_000);
        await PostEsv(owner, year + 1, periodQuarter: 1, periodMonth: null, 570_702);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year + 1}", Json))!;

        Assert.Equal((year + 1, 3), (periods.LimitCrossing!.Year, periods.LimitCrossing.Quarter));
        Assert.Equal([2, 3], periods.Group3Quarters);
        Assert.Equal(
            [new OutsideGroup3PaymentResponse(PaymentKind.Esv, 1, null, 570_702)],
            periods.Balances!.OutsideGroup3Payments);
    }

    [Fact]
    public async Task An_owner_who_never_crossed_has_no_payment_listed_apart()
    {
        const int year = 2085;
        await using var application = At(new DateOnly(year, 11, 25));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 2, 10), 300_000);
        await PostEsv(owner, year, periodQuarter: 3, periodMonth: null, 570_702);
        await PostEsv(owner, year, periodQuarter: null, periodMonth: 8, 190_234);

        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json))!;
        var balances = periods.Balances!;

        Assert.Equal([1, 2, 3, 4], periods.Group3Quarters);
        Assert.Empty(balances.OutsideGroup3Payments);
        Assert.Equal(570_702 + 190_234, balances.Esv.PaidKop);
    }

    [Theory]
    [InlineData(2099, 0)]
    [InlineData(2099, 5)]
    [InlineData(1999, 1)]
    public async Task Back_on_group_3_must_name_a_real_quarter(int year, int quarter)
    {
        await using var application = At(new DateOnly(2099, 1, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/settings", Settings(new DateOnly(2099, 1, 1), new YearQuarter(year, quarter)), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("backOnGroup3From", await response.Content.ReadAsStringAsync());
    }

    private static (int Year, int Quarter, int SwitchFromYear, int SwitchFromQuarter) Of(LimitCrossingResponse? crossing)
    {
        Assert.NotNull(crossing);
        return (crossing.Year, crossing.Quarter, crossing.SwitchFromYear, crossing.SwitchFromQuarter);
    }

    private WebApplicationFactory<Program> At(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero)))));

    private static async Task SetUp(HttpClient owner, int year, YearQuarter? backOnGroup3From = null)
    {
        foreach (var configured in new[] { year, year + 1 })
        {
            var taxYear = new TaxYearConfigRequest(
                864_700, 500, 100, 2_200, 1_500, 1, [85, 100], 19, 40, 10, 15, [], "a test source");
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{configured}", taxYear, Json)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/tax-years/{configured}/verify", null)).StatusCode);
        }

        var response = await owner.PutAsJsonAsync("/api/settings", Settings(new DateOnly(year, 1, 1), backOnGroup3From), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static SettingsRequest Settings(DateOnly registered, YearQuarter? backOnGroup3From) => new(
        registered,
        PaymentMode.Quarterly,
        EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false,
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        [DayOfWeek.Saturday, DayOfWeek.Sunday],
        "uk",
        "system",
        "UAH",
        backOnGroup3From);

    private static async Task PostIncome(HttpClient owner, DateOnly valueDate, long amountKop)
    {
        var request = new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", request, Json)).StatusCode);
    }

    private static async Task PostEsv(HttpClient owner, int periodYear, int? periodQuarter, int? periodMonth, long amountKop)
    {
        var request = new PaymentRequest(
            new DateOnly(periodYear, 6, 1), PaymentKind.Esv, amountKop, periodYear, periodQuarter, periodMonth, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/payments", request, Json)).StatusCode);
    }

    private static async Task<DeclarationResponse> Declaration(HttpClient owner, int year, int quarter)
    {
        var response = await owner.GetAsync($"/api/declarations/{year}/{quarter}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationResponse>(Json))!;
    }
}
