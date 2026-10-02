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

namespace TaxesUa.Api.Tests.Features.Settings;

// The owner registered on 2081-09-28. Tax Code 298.1.2 gives until 2081-10-08 to apply for group 3;
// without that the FOP is on the general system until group 3 starts with a later quarter (298.1.4).
// The clock is in 2081 because the test client drops a session cookie already expired on the real
// clock. Each test puts the owner into the state it needs, since xUnit fixes no order.
public sealed class DpsStatusEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Registered = new(2081, 9, 28);

    private static readonly DateOnly Deadline = new(2081, 10, 8);

    private static readonly DateOnly NextYear = new(2082, 1, 1);

    [Fact]
    public async Task Put_then_get_round_trips_the_status_and_the_fop_form_leaves_it_alone()
    {
        await using var application = At(new DateOnly(2081, 10, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);

        var written = await Put(owner, new DpsStatusRequest(
            Registered, new Group3ConfirmationDto(new DateOnly(2081, 10, 2), "  9123/456  "), true, false, true));
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        await PutSettings(owner, Registered);

        var expected = new DpsStatusResponse(
            Registered,
            new Group3ConfirmationDto(new DateOnly(2081, 10, 2), "9123/456"),
            true,
            false,
            true,
            Registered,
            Registered,
            Deadline);
        Assert.Equal(expected, await written.Content.ReadFromJsonAsync<DpsStatusResponse>(Json));
        Assert.Equal(expected, await Get(owner));
        var raw = await owner.GetStringAsync("/api/settings/dps-status");
        Assert.Contains("\"applicationDeadline\":\"2081-10-08\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Group3_since_a_later_quarter_starts_group_3_there()
    {
        await using var application = At(new DateOnly(2081, 10, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);

        Assert.Equal(HttpStatusCode.OK, (await Put(owner, new DpsStatusRequest(NextYear, null, false, false, false))).StatusCode);

        var status = await Get(owner);
        Assert.Equal((NextYear, NextYear, (Group3ConfirmationDto?)null), (status.Group3Since, status.Group3Start, status.Confirmation));
    }

    public static TheoryData<string, string> Invalid => new()
    {
        { "mid-quarter", "group3Since" },
        { "a quarter before registration", "group3Since" },
        { "the day before registration", "group3Since" },
        { "a blank receipt number", "confirmation.receiptNumber" },
        { "a receipt number over 64", "confirmation.receiptNumber" },
        { "a receipt number with a control character", "confirmation.receiptNumber" },
        { "a receipt before registration", "confirmation.confirmedOn" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task An_invalid_status_is_refused_and_changes_nothing(string label, string errorKey)
    {
        var request = label switch
        {
            "mid-quarter" => Status(new DateOnly(2081, 11, 1)),
            "a quarter before registration" => Status(new DateOnly(2081, 7, 1)),
            "the day before registration" => Status(new DateOnly(2081, 9, 27)),
            "a blank receipt number" => Status(null, new Group3ConfirmationDto(Registered, "   ")),
            "a receipt number over 64" => Status(null, new Group3ConfirmationDto(Registered, new string('7', 65))),
            "a receipt number with a control character" => Status(null, new Group3ConfirmationDto(Registered, "91\u000723")),
            "a receipt before registration" => Status(null, new Group3ConfirmationDto(new DateOnly(2081, 9, 27), "1")),
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
        };
        await using var application = At(new DateOnly(2081, 10, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, Status(null))).StatusCode);

        var response = await Put(owner, request);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, label);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _), $"{label}: no error under {errorKey}");
        Assert.Equal(new DpsStatusResponse(null, null, false, false, false, Registered, Registered, Deadline), await Get(owner));
    }

    [Fact]
    public async Task Without_a_registration_date_neither_group3_since_nor_a_receipt_is_accepted()
    {
        await using var application = At(new DateOnly(2081, 10, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);

        var response = await Put(owner, Status(NextYear, new Group3ConfirmationDto(Registered, "1")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("group3Since", out _));
        Assert.True(errors.TryGetProperty("confirmation.confirmedOn", out _));
        Assert.Equal(new DpsStatusResponse(null, null, false, false, false, null, null, null), await Get(owner));
    }

    [Theory]
    [InlineData("2081-10-01", 7)]
    [InlineData("2081-10-08", 0)]
    public async Task The_dashboard_shows_the_application_deadline_while_it_is_pending(string today, int daysLeft)
    {
        await using var application = At(DateOnly.Parse(today));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, Status(Registered))).StatusCode);

        var group3 = (await Dashboard(owner)).Group3;

        Assert.Equal(new Group3StatusResponse(Registered, false, Deadline, daysLeft, null), group3);
    }

    [Fact]
    public async Task The_dashboard_drops_the_deadline_once_confirmed_or_passed()
    {
        await using var passed = At(new DateOnly(2081, 10, 9));
        using var late = await ApiFixture.SignIn(passed, ApiFixture.AllowedEmail);
        await SetUp(late);
        Assert.Equal(HttpStatusCode.OK, (await Put(late, Status(null))).StatusCode);
        Assert.Equal(new Group3StatusResponse(Registered, false, null, null, null), (await Dashboard(late)).Group3);

        await using var application = At(new DateOnly(2081, 10, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        Assert.Equal(
            HttpStatusCode.OK,
            (await Put(owner, Status(null, new Group3ConfirmationDto(new DateOnly(2081, 10, 1), "1")))).StatusCode);
        Assert.Equal(new Group3StatusResponse(Registered, true, null, null, null), (await Dashboard(owner)).Group3);
    }

    [Fact]
    public async Task Before_group_3_the_year_reports_the_stretch_and_its_income_apart()
    {
        await using var application = At(new DateOnly(2081, 11, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);
        await DeleteTransactions(owner);
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, Status(NextYear))).StatusCode);
        await PostIncome(owner, new DateOnly(2081, 10, 10), 1_000_000);

        var dashboard = await Dashboard(owner);
        var periods = (await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2081", Json))!;

        var stretch = new BeforeGroup3Response(Registered, new DateOnly(2081, 12, 31), 1_000_000);
        Assert.Equal(new Group3StatusResponse(NextYear, false, null, null, stretch), dashboard.Group3);
        Assert.Equal(stretch, periods.Warnings.BeforeGroup3);
        Assert.Empty(periods.Quarters);
        Assert.Equal([1, 2], periods.Group3Quarters);
        Assert.Null((await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2082", Json))!.Warnings.BeforeGroup3);
    }

    [Fact]
    public async Task A_quarter_before_group_3_is_not_ready_and_an_unconfirmed_group_3_only_warns()
    {
        await using var application = At(new DateOnly(2082, 1, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner);

        Assert.Equal(HttpStatusCode.OK, (await Put(owner, Status(NextYear))).StatusCode);
        var before = (await owner.GetFromJsonAsync<DeclarationResponse>("/api/declarations/2081/4", Json))!.Readiness;

        Assert.Equal(HttpStatusCode.OK, (await Put(owner, Status(null))).StatusCode);
        var unconfirmed = (await owner.GetFromJsonAsync<DeclarationResponse>("/api/declarations/2081/4", Json))!.Readiness;
        var filed = await owner.PutAsJsonAsync(
            "/api/declarations/2081/4/filing", new DeclarationFilingRequest(new DateOnly(2082, 1, 15), DeclarationType.Reporting), Json);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/declarations/2081/4/filing")).StatusCode);

        Assert.Equal((true, false, false, false), (before.BeforeGroup3, before.OutsideGroup3, before.Group3Confirmed, before.Ready));
        Assert.Equal((false, false, false), (unconfirmed.BeforeGroup3, unconfirmed.OutsideGroup3, unconfirmed.Group3Confirmed));
        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);
    }

    private static DpsStatusRequest Status(DateOnly? group3Since, Group3ConfirmationDto? confirmation = null) =>
        new(group3Since, confirmation, false, false, false);

    private WebApplicationFactory<Program> At(DateOnly kyivToday) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(kyivToday.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero)))));

    private static Task<HttpResponseMessage> Put(HttpClient owner, DpsStatusRequest request) =>
        owner.PutAsJsonAsync("/api/settings/dps-status", request, Json);

    private static async Task<DpsStatusResponse> Get(HttpClient owner) =>
        (await owner.GetFromJsonAsync<DpsStatusResponse>("/api/settings/dps-status", Json))!;

    private static async Task<DashboardResponse> Dashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static async Task SetUp(HttpClient owner)
    {
        foreach (var year in new[] { 2081, 2082 })
        {
            var taxYear = new TaxYearConfigRequest(
                864_700, 500, 100, 2_200, 1_500, 1_167, [85, 100], 19, 40, 10, 15, 10, [], "a test source");
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", taxYear, Json)).StatusCode);
        }

        await PutSettings(owner, Registered);
    }

    private static async Task PutSettings(HttpClient owner, DateOnly? registered)
    {
        var settings = new SettingsRequest(
            registered,
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
    }

    private static async Task PostIncome(HttpClient owner, DateOnly valueDate, long amountKop)
    {
        var response = await owner.PostAsJsonAsync(
            "/api/transactions",
            new TransactionRequest(valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task DeleteTransactions(HttpClient owner)
    {
        var list = (await owner.GetFromJsonAsync<TransactionListResponse>("/api/transactions?year=2081", Json))!;
        foreach (var row in list.Items)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{row.Id}")).StatusCode);
        }
    }
}
