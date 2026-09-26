using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;

namespace TaxesUa.Api.Tests.Features.Periods;

// ApiFixture is an IClassFixture, so this class owns its own database. Only the settings-mutating
// tests write a row for the owner, and each of those resets what it wrote so the others still see
// the defaults, since xUnit fixes no order between them.
public sealed class PeriodsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // The web speaks this dialect, generated from the OpenAPI document, so the tests speak it too
    // rather than falling back to the numeric enums a default client would send.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task The_2026_seed_with_default_settings_matches_the_reference_table()
    {
        using var client = await SignIn();

        var periods = await client.GetFromJsonAsync<PeriodsResponse>("/api/periods/2026", Json);

        Assert.NotNull(periods);
        Assert.Equal(2026, periods.Year);
        Assert.Equal(4, periods.Quarters.Length);

        AssertQuarter(periods, 1, "2026-04-19", "2026-04-20", "2026-05-10", "2026-05-11", "2026-05-20", "2026-05-20");
        AssertQuarter(periods, 2, "2026-07-19", "2026-07-20", "2026-08-09", "2026-08-10", "2026-08-19", "2026-08-19");
        AssertQuarter(periods, 3, "2026-10-19", "2026-10-19", "2026-11-09", "2026-11-09", "2026-11-19", "2026-11-19");
        AssertQuarter(periods, 4, "2027-01-19", "2027-01-19", "2027-02-09", "2027-02-09", "2027-02-19", "2027-02-19");
    }

    [Fact]
    public async Task A_settings_write_changes_the_tax_payment_deadline()
    {
        using var client = await SignIn();
        try
        {
            var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
            var request = ToRequest(settings!) with { TaxPaymentCountsFromStatutoryDeclarationDate = false };
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>("/api/periods/2026", Json);

            var q1 = Assert.Single(periods!.Quarters, quarter => quarter.Quarter == 1);
            Assert.Equal(Date("2026-05-21"), q1.Deadlines.TaxPayment.Due);
        }
        finally
        {
            await ResetSettings(client);
        }
    }

    // 2093-04-20 (a Monday) is the Q1 ESV statutory date TaxYearRequest's EsvDeadlineDay=20 produces
    // for this year. Naming it as a holiday pushes the due date to the next weekday, Tuesday the 21st.
    [Fact]
    public async Task A_configured_holiday_shifts_a_deadline()
    {
        const int year = 2093;
        using var client = await SignIn();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync(
                $"/api/tax-years/{year}",
                TaxYearRequest(holidays: [Date("2093-04-20")]))).StatusCode);

        var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

        var q1 = Assert.Single(periods!.Quarters, quarter => quarter.Quarter == 1);
        Assert.Equal(Date("2093-04-20"), q1.Deadlines.Esv.Statutory);
        Assert.Equal(Date("2093-04-21"), q1.Deadlines.Esv.Due);
    }

    [Fact]
    public async Task A_mid_quarter_registration_hides_only_the_earlier_quarters()
    {
        using var client = await SignIn();
        try
        {
            var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
            var request = ToRequest(settings!) with { FopRegistrationDate = Date("2026-04-15") };
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>("/api/periods/2026", Json);

            Assert.Equal(new[] { 2, 3, 4 }, periods!.Quarters.Select(quarter => quarter.Quarter).ToArray());
        }
        finally
        {
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task A_registration_date_in_a_later_year_hides_every_quarter()
    {
        using var client = await SignIn();
        try
        {
            var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
            var request = ToRequest(settings!) with { FopRegistrationDate = Date("2027-01-01") };
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>("/api/periods/2026", Json);

            Assert.Empty(periods!.Quarters);
        }
        finally
        {
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task A_missing_year_is_not_found()
    {
        using var client = await SignIn();

        var response = await client.GetAsync("/api/periods/2097");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_the_route_is_unauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/periods/2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertQuarter(
        PeriodsResponse periods,
        int quarter,
        string esvStatutory,
        string esvDue,
        string declarationStatutory,
        string declarationDue,
        string taxPaymentStatutory,
        string taxPaymentDue)
    {
        var found = Assert.Single(periods.Quarters, q => q.Quarter == quarter);
        Assert.Equal(Date(esvStatutory), found.Deadlines.Esv.Statutory);
        Assert.Equal(Date(esvDue), found.Deadlines.Esv.Due);
        Assert.Equal(Date(declarationStatutory), found.Deadlines.Declaration.Statutory);
        Assert.Equal(Date(declarationDue), found.Deadlines.Declaration.Due);
        Assert.Equal(Date(taxPaymentStatutory), found.Deadlines.TaxPayment.Statutory);
        Assert.Equal(Date(taxPaymentDue), found.Deadlines.TaxPayment.Due);
    }

    private static SettingsRequest ToRequest(SettingsResponse response) => new(
        response.FopRegistrationDate,
        response.PaymentMode,
        response.EsvRegistrationMonthPolicy,
        response.EsvExempt,
        response.TaxPaymentCountsFromStatutoryDeclarationDate,
        response.ShiftTaxPaymentFromWeekend,
        response.WeekendDays,
        response.Locale,
        response.Theme,
        response.DefaultCurrency);

    private static TaxYearConfigRequest TaxYearRequest(DateOnly[] holidays) => new(
        800_000L,
        600,
        200,
        2100,
        1600,
        1200,
        [80, 95],
        20,
        41,
        11,
        16,
        holidays,
        "a test source");

    private static DateOnly Date(string iso) => DateOnly.Parse(iso);

    private async Task ResetSettings(HttpClient client)
    {
        var defaults = new SettingsRequest(
            FopRegistrationDate: null,
            PaymentMode: PaymentMode.Quarterly,
            EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
            Locale: "uk",
            Theme: "system",
            DefaultCurrency: "UAH");

        await client.PutAsJsonAsync("/api/settings", defaults, Json);
    }

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
