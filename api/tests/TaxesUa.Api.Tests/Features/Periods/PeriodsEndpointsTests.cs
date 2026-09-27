using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

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

    // 2009-04-20 (a Monday) is the Q1 ESV statutory date TaxYearRequest's EsvDeadlineDay=20 produces
    // for this year. Naming it as a holiday pushes the due date to the next weekday, Tuesday the 21st.
    [Fact]
    public async Task A_configured_holiday_shifts_a_deadline()
    {
        const int year = 2009;
        using var client = await SignIn();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync(
                $"/api/tax-years/{year}",
                TaxYearRequest(holidays: [Date("2009-04-20")]))).StatusCode);

        var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

        var q1 = Assert.Single(periods!.Quarters, quarter => quarter.Quarter == 1);
        Assert.Equal(Date("2009-04-20"), q1.Deadlines.Esv.Statutory);
        Assert.Equal(Date("2009-04-21"), q1.Deadlines.Esv.Due);
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

    // Registration 2012-02-10 leaves Q1 two ESV months. The January receipt predates it, the Q3
    // refund is smaller than the income already taxed, and the own transfer is not income.
    [Fact]
    public async Task Accruals_match_the_engine_on_the_same_data()
    {
        const int year = 2012;
        using var client = await SignIn();
        var config = TaxYearRequest(holidays: []);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tax-years/{year}", config)).StatusCode);
        try
        {
            var settings = await SetRegistrationDate(client, Date("2012-02-10"));
            await PostTransaction(client, "2012-01-20", 5_000_000, TransactionKind.Income);
            await PostTransaction(client, "2012-02-15", 10_000_000, TransactionKind.Income);
            await PostTransaction(client, "2012-05-05", 20_000_000, TransactionKind.Income);
            await PostTransaction(client, "2012-08-01", 3_000_000, TransactionKind.RefundToClient);
            await PostTransaction(client, "2012-08-02", 99_999, TransactionKind.OwnTransfer, "between my accounts");

            var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

            var expected = Accruals.ForYear(
                year,
                [
                    new TransactionInput.Income(Date("2012-01-20"), 5_000_000),
                    new TransactionInput.Income(Date("2012-02-15"), 10_000_000),
                    new TransactionInput.Income(Date("2012-05-05"), 20_000_000),
                    new TransactionInput.RefundToClient(Date("2012-08-01"), 3_000_000),
                    new TransactionInput.NonIncome(Date("2012-08-02"), 99_999, NonIncomeKind.OwnTransfer, "x"),
                ],
                new TaxYearConfigInput(
                    config.MinWageKop,
                    config.SingleTaxRateBp,
                    config.MilitaryLevyRateBp,
                    config.EsvRateBp,
                    config.EsvDeadlineDay,
                    config.DeclarationDays,
                    config.TaxPaymentDaysAfterDeclaration,
                    config.Holidays,
                    config.MinWageKop * config.IncomeLimitMinWages,
                    config.ExcessRateBp,
                    config.LimitWarnThresholdsPct),
                new FopSettingsInput(
                    settings.WeekendDays,
                    settings.TaxPaymentCountsFromStatutoryDeclarationDate,
                    settings.ShiftTaxPaymentFromWeekend,
                    Date("2012-02-10"),
                    TaxesUa.Engine.EsvRegistrationMonthPolicy.FullMonth,
                    EsvExempt: false));

            Assert.Equal(4, periods!.Quarters.Length);
            foreach (var engine in expected.Quarters)
            {
                var actual = Assert.Single(periods.Quarters, q => q.Quarter == engine.Income.Quarter);
                Assert.Equal(engine.Income.IncomeKop, actual.IncomeKop);
                Assert.Equal(engine.SingleTaxKop, actual.SingleTaxKop);
                Assert.Equal(engine.MilitaryLevyKop, actual.MilitaryLevyKop);
                Assert.Equal(engine.EsvKop, actual.EsvKop);
                Assert.Equal(engine.TotalKop, actual.TotalKop);
                Assert.Equal(engine.Income.CumulativeIncomeKop, actual.CumulativeIncomeKop);
                Assert.Equal(engine.CumulativeSingleTaxKop, actual.CumulativeSingleTaxKop);
                Assert.Equal(engine.CumulativeMilitaryLevyKop, actual.CumulativeMilitaryLevyKop);
            }

            // The same figures by hand, so a fault shared by the api and the engine still fails here.
            // ESV is 21% of 8,000.00 = 1,680.00 a month.
            var q1 = periods.Quarters[0];
            Assert.Equal(
                (10_000_000L, 600_000L, 200_000L, 336_000L, 1_136_000L),
                (q1.IncomeKop, q1.SingleTaxKop, q1.MilitaryLevyKop, q1.EsvKop, q1.TotalKop));
            var q3 = periods.Quarters[2];
            Assert.Equal(
                (-3_000_000L, -180_000L, -60_000L, 504_000L),
                (q3.IncomeKop, q3.SingleTaxKop, q3.MilitaryLevyKop, q3.EsvKop));
            var q4 = periods.Quarters[3];
            Assert.Equal(
                (27_000_000L, 1_620_000L, 540_000L),
                (q4.CumulativeIncomeKop, q4.CumulativeSingleTaxKop, q4.CumulativeMilitaryLevyKop));
            Assert.Equal(periods.Quarters.Sum(q => q.SingleTaxKop), q4.CumulativeSingleTaxKop);

            Assert.True(periods.Warnings.TaxYearUnverified);
            Assert.False(periods.Warnings.FopRegistrationDateNotSet);
            Assert.Equal(1, periods.Warnings.ExcludedOperationCount);
            Assert.Empty(periods.Warnings.NegativeCumulativeTaxQuarters);
        }
        finally
        {
            await DeleteTransactions(client, year);
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task A_verified_year_carries_no_unverified_warning()
    {
        const int year = 2015;
        using var client = await SignIn();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(holidays: []))).StatusCode);

        var before = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/tax-years/{year}/verify", null)).StatusCode);
        var after = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

        Assert.True(before!.Warnings.TaxYearUnverified);
        Assert.False(after!.Warnings.TaxYearUnverified);
    }

    [Fact]
    public async Task Without_a_registration_date_every_figure_is_zero_and_says_why()
    {
        using var client = await SignIn();

        var periods = await client.GetFromJsonAsync<PeriodsResponse>("/api/periods/2026", Json);

        Assert.True(periods!.Warnings.FopRegistrationDateNotSet);
        Assert.All(periods.Quarters, quarter => Assert.Equal((0L, 0L), (quarter.TotalKop, quarter.CumulativeIncomeKop)));
    }

    [Fact]
    public async Task A_refund_larger_than_the_income_so_far_names_the_negative_quarters()
    {
        const int year = 2014;
        using var client = await SignIn();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(holidays: []))).StatusCode);
        try
        {
            await SetRegistrationDate(client, Date("2014-01-01"));
            await PostTransaction(client, "2014-02-01", 1_000_000, TransactionKind.Income);
            await PostTransaction(client, "2014-03-01", 3_000_000, TransactionKind.RefundToClient);
            await PostTransaction(client, "2014-07-01", 5_000_000, TransactionKind.Income);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

            Assert.Equal(new[] { 1, 2 }, periods!.Warnings.NegativeCumulativeTaxQuarters);
            Assert.Equal(-2_000_000, periods.Quarters[1].CumulativeIncomeKop);
        }
        finally
        {
            await DeleteTransactions(client, year);
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task A_refund_of_a_pre_registration_receipt_manufactures_no_tax_credit()
    {
        const int year = 2011;
        using var client = await SignIn();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(holidays: []))).StatusCode);
        try
        {
            await SetRegistrationDate(client, Date("2011-03-01"));
            var receipt = await PostTransaction(client, "2011-02-10", 10_000_000, TransactionKind.Income);
            await PostTransaction(
                client, "2011-04-15", 10_000_000, TransactionKind.RefundToClient, refundsTransactionId: receipt);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

            var q2 = periods!.Quarters[1];
            Assert.Equal((0L, 0L, 0L), (q2.IncomeKop, q2.SingleTaxKop, q2.MilitaryLevyKop));
            Assert.Empty(periods.Warnings.NegativeCumulativeTaxQuarters);
            Assert.Equal(2, periods.Warnings.ExcludedOperationCount);
        }
        finally
        {
            await DeleteTransactions(client, year);
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task An_unlinked_refund_after_registration_still_reduces_income()
    {
        const int year = 2010;
        using var client = await SignIn();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(holidays: []))).StatusCode);
        try
        {
            await SetRegistrationDate(client, Date("2010-03-01"));
            await PostTransaction(client, "2010-02-10", 10_000_000, TransactionKind.Income);
            await PostTransaction(client, "2010-04-15", 10_000_000, TransactionKind.RefundToClient);

            var periods = await client.GetFromJsonAsync<PeriodsResponse>($"/api/periods/{year}", Json);

            var q2 = periods!.Quarters[1];
            Assert.Equal(-10_000_000, q2.IncomeKop);
            Assert.True(q2.SingleTaxKop < 0);
            Assert.Equal(new[] { 2, 3, 4 }, periods.Warnings.NegativeCumulativeTaxQuarters);
            Assert.Equal(1, periods.Warnings.ExcludedOperationCount);
        }
        finally
        {
            await DeleteTransactions(client, year);
            await ResetSettings(client);
        }
    }

    [Fact]
    public async Task A_missing_year_is_not_found()
    {
        using var client = await SignIn();

        var response = await client.GetAsync("/api/periods/2017");

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

    private static async Task<SettingsResponse> SetRegistrationDate(HttpClient client, DateOnly date)
    {
        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        var request = ToRequest(settings!) with { FopRegistrationDate = date };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);
        return settings!;
    }

    private static async Task<Guid> PostTransaction(
        HttpClient client,
        string valueDate,
        long amountMinor,
        TransactionKind kind,
        string? nonIncomeReason = null,
        Guid? refundsTransactionId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            new TransactionRequest(
                Date(valueDate),
                amountMinor,
                Currency.UAH,
                null,
                kind,
                nonIncomeReason,
                null,
                null,
                null,
                refundsTransactionId),
            Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!.Id;
    }

    private static async Task DeleteTransactions(HttpClient client, int year)
    {
        var list = await client.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?year={year}", Json);
        foreach (var item in list!.Items)
        {
            await client.DeleteAsync($"/api/transactions/{item.Id}");
        }
    }

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
