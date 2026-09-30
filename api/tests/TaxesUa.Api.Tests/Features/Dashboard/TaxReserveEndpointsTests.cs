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

// Each test owns a year of its own, two apart from the next: the ledger runs from the registration year
// through every consecutive configured year, so a year left unconfigured between two tests keeps one
// test's receipts and payments out of another's ledger, whatever order xUnit runs them in.
public sealed class TaxReserveEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const long EsvMonthKop = 190_234;

    private const long EsvQuarterKop = 570_702;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_reserve_is_what_has_accrued_and_is_unpaid_grouped_by_due_date_in_both_modes(bool monthlyAdvances)
    {
        var year = monthlyAdvances ? 2083 : 2081;
        var mode = monthlyAdvances ? PaymentMode.MonthlyAdvance : PaymentMode.Quarterly;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, mode);
        await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);

        var reserve = (await Get(client)).Reserve;

        Assert.NotNull(reserve);
        Assert.Equal(60_000 + EsvQuarterKop + (2 * EsvMonthKop), reserve.TotalKop);
        Assert.Equal(
            [
                (Deadlines(year, 1).Esv.Due, ObligationStatus.Overdue, DaysFrom(May1(year), Deadlines(year, 1).Esv.Due), 0L, 0L, EsvQuarterKop),
                (Deadlines(year, 1).TaxPayment.Due, ObligationStatus.Upcoming, DaysFrom(May1(year), Deadlines(year, 1).TaxPayment.Due), 50_000L, 10_000L, 0L),
                (Deadlines(year, 2).Esv.Due, ObligationStatus.Upcoming, DaysFrom(May1(year), Deadlines(year, 2).Esv.Due), 0L, 0L, 2 * EsvMonthKop),
            ],
            reserve.Dues.Select(due => (due.DueDate, due.Status, due.DaysLeft, due.SingleTaxKop, due.MilitaryLevyKop, due.EsvKop)));
    }

    [Fact]
    public async Task A_payment_reduces_the_reserve_by_the_allocation_and_a_full_payment_empties_it()
    {
        const int year = 2085;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);

        await PostPayment(client, year, PaymentKind.Esv, EsvQuarterKop + 100_000);
        await PostPayment(client, year, PaymentKind.SingleTax, 20_000);

        var partly = (await Get(client)).Reserve!;

        Assert.Equal(
            [
                (Deadlines(year, 1).TaxPayment.Due, 30_000L, 10_000L, 0L),
                (Deadlines(year, 2).Esv.Due, 0L, 0L, (2 * EsvMonthKop) - 100_000),
            ],
            partly.Dues.Select(due => (due.DueDate, due.SingleTaxKop, due.MilitaryLevyKop, due.EsvKop)));

        await PostPayment(client, year, PaymentKind.Esv, (2 * EsvMonthKop) - 100_000);
        await PostPayment(client, year, PaymentKind.SingleTax, 30_000);
        await PostPayment(client, year, PaymentKind.MilitaryLevy, 10_000);

        var paid = (await Get(client)).Reserve!;

        Assert.Equal(0, paid.TotalKop);
        Assert.Empty(paid.Dues);
    }

    [Fact]
    public async Task Each_owner_sees_only_their_own_reserve()
    {
        const int year = 2087;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, PaymentMode.Quarterly);
        await SetUp(other, year, PaymentMode.Quarterly);
        await PostIncome(owner, new DateOnly(year, 2, 10), 1_000_000);
        await PostPayment(owner, year, PaymentKind.Esv, 3 * EsvQuarterKop);

        var ownerReserve = (await Get(owner)).Reserve!;
        var otherReserve = (await Get(other)).Reserve!;

        Assert.Equal(60_000L, ownerReserve.TotalKop);
        Assert.Equal(5 * EsvMonthKop, otherReserve.TotalKop);
        Assert.All(otherReserve.Dues, due => Assert.Equal(0, due.SingleTaxKop + due.MilitaryLevyKop));
    }

    [Fact]
    public async Task Without_a_registration_date_there_is_no_reserve()
    {
        const int year = 2089;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly, registered: false);

        Assert.Null((await Get(client)).Reserve);
    }

    [Fact]
    public async Task A_receipt_shows_its_set_aside_and_a_transfer_shows_zero()
    {
        const int year = 2091;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        var receipt = await PostIncome(client, new DateOnly(year, 2, 10), 1_234_567);
        var refund = await PostRefund(client, new DateOnly(year, 3, 1), 200_000, receipt.Id);
        var transfer = await PostTransfer(client, new DateOnly(year, 3, 2), 500_000);

        var list = await client.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?year={year}", Json);

        var byId = list!.Items.ToDictionary(item => item.Id);
        Assert.Equal(new SetAsideResponse(61_728, 12_346), byId[receipt.Id].SetAside);
        Assert.Equal(new SetAsideResponse(-10_000, -2_000), byId[refund.Id].SetAside);
        Assert.Equal(new SetAsideResponse(0, 0), byId[transfer.Id].SetAside);
        Assert.Equal(new SetAsideResponse(61_728, 12_346), receipt.SetAside);
    }

    [Fact]
    public async Task A_receipt_before_registration_and_its_refund_show_none()
    {
        const int year = 2093;
        await using var application = At(new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly, registrationDate: new DateOnly(year, 3, 1));
        var early = await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);
        var refund = await PostRefund(client, new DateOnly(year, 4, 1), 100_000, early.Id);

        var list = await client.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?year={year}", Json);

        Assert.All(list!.Items, item => Assert.Null(item.SetAside));
        Assert.Null(early.SetAside);
        Assert.Null(refund.SetAside);
    }

    [Fact]
    public async Task A_receipt_uses_the_rates_of_its_own_year()
    {
        const int year = 2095;
        await using var application = At(new DateTimeOffset(year + 1, 5, 1, 9, 0, 0, TimeSpan.Zero));
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await PutYear(client, year + 1, singleTaxBp: 300, levyBp: 150);
        var first = await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);
        var second = await PostIncome(client, new DateOnly(year + 1, 2, 10), 1_000_000);

        Assert.Equal(new SetAsideResponse(50_000, 10_000), first.SetAside);
        Assert.Equal(new SetAsideResponse(30_000, 15_000), second.SetAside);
    }

    private static readonly TaxYearConfigInput Config = new(
        864_700, 500, 100, 2_200, 19, 40, 10, [], 864_700 * 1_167, 1_500, [85, 100]);

    private static readonly FopSettingsInput Fop = new(
        [DayOfWeek.Saturday, DayOfWeek.Sunday], true, true, null, Engine.EsvRegistrationMonthPolicy.FullMonth, false);

    private static QuarterDeadlines Deadlines(int year, int quarter) =>
        DeadlineCalendar.ForQuarter(year, quarter, Config, Fop);

    private static DateOnly May1(int year) => new(year, 5, 1);

    private static int DaysFrom(DateOnly today, DateOnly due) => due.DayNumber - today.DayNumber;

    private WebApplicationFactory<Program> At(DateTimeOffset utcNow) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(utcNow))));

    private static async Task<DashboardResponse> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static Task<TransactionResponse> PostIncome(HttpClient client, DateOnly valueDate, long amountKop) =>
        Post(client, new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null));

    private static Task<TransactionResponse> PostRefund(HttpClient client, DateOnly valueDate, long amountKop, Guid receiptId) =>
        Post(client, new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.RefundToClient, null, null, null, null, receiptId));

    private static Task<TransactionResponse> PostTransfer(HttpClient client, DateOnly valueDate, long amountKop) =>
        Post(client, new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.OwnTransfer, "between own accounts", null, null, null, null));

    private static async Task<TransactionResponse> Post(HttpClient client, TransactionRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private static async Task PostPayment(HttpClient client, int year, PaymentKind kind, long amountKop)
    {
        var response = await client.PostAsJsonAsync(
            "/api/payments", new PaymentRequest(new DateOnly(year, 5, 1), kind, amountKop, year, 1, null, null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The 2026 parameters, so ESV is 1,902.34 a month.
    private static async Task PutYear(HttpClient client, int year, int singleTaxBp = 500, int levyBp = 100)
    {
        var config = new TaxYearConfigRequest(
            864_700, singleTaxBp, levyBp, 2_200, 1_500, 1_167, [85, 100], 19, 40, 10, 15, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tax-years/{year}", config, Json)).StatusCode);
    }

    private static async Task SetUp(
        HttpClient client, int year, PaymentMode mode, bool registered = true, DateOnly? registrationDate = null)
    {
        await PutYear(client, year);
        var request = new SettingsRequest(
            FopRegistrationDate: registered ? registrationDate ?? new DateOnly(year, 1, 1) : null,
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
