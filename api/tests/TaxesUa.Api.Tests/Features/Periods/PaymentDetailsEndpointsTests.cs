using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;
using PaymentMode = TaxesUa.Api.Features.Settings.PaymentMode;

namespace TaxesUa.Api.Tests.Features.Periods;

// The details a Pay panel shows (#99, Rule 16). A restore of an empty file gives each test a clean owner.
public sealed class PaymentDetailsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Empty =
        """{"schemaVersion":19,"settings":null,"clients":[],"transactions":[],"budgetPayments":[],"bankAccounts":[],"importBatches":[],"budgetPaymentCandidates":[],"invoicingDetails":null,"invoices":[],"declarationDetails":null,"declarationFilings":[],"declarationFiles":[],"treasuryAccounts":[],"notificationChannels":[],"reserveJar":null}""";

    private const string Iban = "UA358999980333159998000026011";

    private const string LearnedIban = "UA018999980333159998000026011";

    private const string QuarterQuery = "kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=123456";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_manual_account_gives_the_recipient_and_the_purpose_of_the_quarter()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var details = await Details(owner, QuarterQuery);

        Assert.Equal(
            (PaymentKind.SingleTax, 2026, 3, (int?)null, 123_456L, "101 єдиний податок за III квартал 2026 року"),
            (details.Kind, details.PeriodYear, details.PeriodQuarter, details.PeriodMonth, details.AmountKop, details.Purpose));
        Assert.Equal(
            (Iban, "ГУ ДПС у м.Києві", "43141912", TreasuryAccountSource.Manual),
            (details.Recipient!.Iban, details.Recipient.Name, details.Recipient.Code, details.Recipient.Source));
        Assert.Empty(details.Missing);
    }

    [Fact]
    public async Task Without_an_amount_the_recipient_and_purpose_come_without_an_amount_or_a_qr()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var details = await Details(owner, "kind=SingleTax&periodYear=2026&periodQuarter=3");

        Assert.Equal((Iban, (long?)null, (string?)null), (details.Recipient!.Iban, details.AmountKop, details.QrContent));
        Assert.Equal("101 єдиний податок за III квартал 2026 року", details.Purpose);
        Assert.Empty(details.Missing);
    }

    [Fact]
    public async Task Without_an_amount_a_kind_with_no_account_still_lists_what_is_missing()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var details = await Details(owner, "kind=Esv&periodYear=2026&periodQuarter=3");

        Assert.Null(details.Recipient);
        Assert.Equal(["iban", "recipientName", "recipientCode"], details.Missing);
    }

    [Fact]
    public async Task A_month_gives_the_purpose_of_the_month()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.Esv, "ГУ ДПС у м.Києві", "43141912");

        var details = await Details(owner, "kind=Esv&periodYear=2026&periodMonth=9&amountKop=190234");

        Assert.Equal("101 єдиний внесок за вересень 2026 року", details.Purpose);
        Assert.Equal((null, 9, 190_234L), (details.PeriodQuarter, details.PeriodMonth, details.AmountKop));
        Assert.Equal(Iban, details.Recipient!.Iban);
    }

    [Fact]
    public async Task A_manual_account_wins_over_a_learned_one_and_a_learned_one_is_used_alone()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await SeedLearned(ApiFixture.AllowedEmail, PaymentKind.SingleTax, "ГУК Київ", "37993783");
        await SeedLearned(ApiFixture.AllowedEmail, PaymentKind.Esv, "ГУК Київ", "37993783");
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var manual = await Details(owner, QuarterQuery);
        var learned = await Details(owner, "kind=Esv&periodYear=2026&periodQuarter=3&amountKop=570702");

        Assert.Equal(
            (TreasuryAccountSource.Manual, Iban, "ГУ ДПС у м.Києві", "43141912"),
            (manual.Recipient!.Source, manual.Recipient.Iban, manual.Recipient.Name, manual.Recipient.Code));
        Assert.Equal(
            (TreasuryAccountSource.Learned, LearnedIban, "ГУК Київ", "37993783"),
            (learned.Recipient!.Source, learned.Recipient.Iban, learned.Recipient.Name, learned.Recipient.Code));
    }

    [Fact]
    public async Task Without_an_account_all_three_details_are_missing_and_no_recipient_is_given()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var details = await Details(owner, QuarterQuery);

        Assert.Null(details.Recipient);
        Assert.Equal(["iban", "recipientName", "recipientCode"], details.Missing);
        Assert.Equal("101 єдиний податок за III квартал 2026 року", details.Purpose);
        Assert.Equal(123_456, details.AmountKop);
        Assert.Null(details.QrContent);
    }

    [Fact]
    public async Task A_complete_recipient_gives_the_nbu_qr_of_the_same_details()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912");

        var details = await Details(owner, "kind=MilitaryLevy&periodYear=2026&periodQuarter=3&amountKop=123456");

        Assert.Equal(
            ["BCD", "003", "1", "UCT", "", "ГУ ДПС у м.Києві", Iban, "UAH1234.56", "43141912", NbuQr.CategoryPurpose, "",
                "101 військовий збір за III квартал 2026 року", "", "FEFF", "", "", ""],
            QrFields(details.QrContent!));
    }

    [Fact]
    public async Task Another_amount_rebuilds_the_qr()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var owed = await Details(owner, QuarterQuery);
        var ahead = await Details(owner, "kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=300000");

        Assert.Equal("UAH1234.56", QrFields(owed.QrContent!)[7]);
        Assert.Equal("UAH3000", QrFields(ahead.QrContent!)[7]);
    }

    [Fact]
    public async Task An_amount_beyond_the_qr_format_keeps_the_recipient_and_gives_no_qr()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var details = await Details(owner, "kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=100000000000");

        Assert.NotNull(details.Recipient);
        Assert.Null(details.QrContent);
    }

    [Fact]
    public async Task A_learned_account_without_a_code_lists_it_as_missing_and_gives_no_recipient()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await SeedLearned(ApiFixture.AllowedEmail, PaymentKind.SingleTax, "ГУК Київ", null);

        var details = await Details(owner, QuarterQuery);

        Assert.Null(details.Recipient);
        Assert.Equal(["recipientCode"], details.Missing);
        Assert.Null(details.QrContent);
    }

    [Theory]
    [InlineData("kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=0", "amountKop")]
    [InlineData("kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=-5", "amountKop")]
    [InlineData("kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=100000000000001", "amountKop")]
    [InlineData("kind=SingleTax&periodYear=2026&periodQuarter=3&periodMonth=9&amountKop=100", "periodQuarter")]
    [InlineData("kind=SingleTax&periodYear=2026&amountKop=100", "periodQuarter")]
    [InlineData("kind=SingleTax&periodYear=2026&periodQuarter=5&amountKop=100", "periodQuarter")]
    [InlineData("kind=SingleTax&periodYear=2026&periodMonth=13&amountKop=100", "periodMonth")]
    [InlineData("kind=SingleTax&periodYear=1999&periodQuarter=3&amountKop=100", "periodYear")]
    [InlineData("kind=7&periodYear=2026&periodQuarter=3&amountKop=100", "kind")]
    public async Task An_invalid_request_is_rejected_with_the_field(string query, string rejectedField)
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.GetAsync($"/api/payment-details?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        ProblemAssert.Rejects(problem, rejectedField);
    }

    [Fact]
    public async Task A_fractional_amount_is_rejected()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.GetAsync("/api/payment-details?kind=SingleTax&periodYear=2026&periodQuarter=3&amountKop=12.5");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_year_without_a_tax_configuration_is_refused()
    {
        const int year = 2003;
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await database.TaxYearConfigs.AnyAsync(config => config.Year == year));
        }

        var response = await owner.GetAsync($"/api/payment-details?kind=SingleTax&periodYear={year}&periodQuarter=3&amountKop=100");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("periodQuarter=3", HttpStatusCode.Conflict)]
    [InlineData("periodMonth=8", HttpStatusCode.Conflict)]
    [InlineData("periodQuarter=2", HttpStatusCode.OK)]
    [InlineData("periodMonth=6", HttpStatusCode.OK)]
    public async Task A_period_after_the_limit_crossing_is_refused(string period, HttpStatusCode expected)
    {
        const int year = 2025;
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await SetUpCrossedInQ2(owner, year);

        var response = await owner.GetAsync($"/api/payment-details?kind=SingleTax&periodYear={year}&{period}&amountKop=100");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_the_details_are_refused()
    {
        var response = await fixture.CreateClient().GetAsync($"/api/payment-details?{QuarterQuery}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_sees_only_their_own_account()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        using var stranger = await SignInEmpty(app, ApiFixture.SecondAllowedEmail);
        await PutManual(owner, PaymentKind.SingleTax, "ГУ ДПС у м.Києві", "43141912");

        var strangerDetails = await Details(stranger, QuarterQuery);
        Assert.Null(strangerDetails.Recipient);
        Assert.Equal(["iban", "recipientName", "recipientCode"], strangerDetails.Missing);

        await PutManual(stranger, PaymentKind.SingleTax, "ГУК Львів", "37993783");
        Assert.Equal("ГУК Львів", (await Details(stranger, QuarterQuery)).Recipient!.Name);
        Assert.Equal("ГУ ДПС у м.Києві", (await Details(owner, QuarterQuery)).Recipient!.Name);
    }

    // The Q4 2026 levy is due 2027-02-19 (40 days after the quarter for the declaration, 10 more for the
    // payment). The panel pays today, so an account ending 2026-12-31 is judged on today: valid, with a
    // warning, until that day, and expired after it.
    private const string Q4Levy = "kind=MilitaryLevy&periodYear=2026&periodQuarter=4&amountKop=123456";

    private const string Q3Levy = "kind=MilitaryLevy&periodYear=2026&periodQuarter=3&amountKop=123456";

    [Fact]
    public async Task A_q4_levy_paid_in_december_into_an_account_ending_in_december_is_shown_with_a_warning()
    {
        await using var app = AppOn(new DateOnly(2026, 12, 1));
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912", new DateOnly(2026, 12, 31));

        var q4 = await Details(owner, Q4Levy);

        Assert.Equal(
            (new DateOnly(2026, 12, 31), PaymentAccountExpiryState.ExpiresBeforeDue),
            (q4.Expiry!.ValidUntil, q4.Expiry.State));
        Assert.Equal(Iban, q4.Recipient!.Iban);
        Assert.NotNull(q4.QrContent);
    }

    [Fact]
    public async Task A_q4_levy_paid_in_february_2027_against_an_account_ending_2026_12_31_is_expired_with_no_recipient_or_qr()
    {
        await using var app = AppOn(new DateOnly(2027, 2, 10));
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912", new DateOnly(2026, 12, 31));

        var q4 = await Details(owner, Q4Levy);

        Assert.Equal(
            (new DateOnly(2026, 12, 31), PaymentAccountExpiryState.Expired),
            (q4.Expiry!.ValidUntil, q4.Expiry.State));
        Assert.Null(q4.Recipient);
        Assert.Null(q4.QrContent);
        Assert.Empty(q4.Missing);
        Assert.Equal("101 військовий збір за IV квартал 2026 року", q4.Purpose);
    }

    [Fact]
    public async Task An_overdue_payment_made_after_the_end_is_expired_and_one_due_and_made_before_it_is_not_flagged()
    {
        await using var inTime = AppOn(new DateOnly(2026, 12, 1));
        using var owner = await SignInEmpty(inTime, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912", new DateOnly(2026, 12, 31));

        var q3 = await Details(owner, Q3Levy);

        Assert.Null(q3.Expiry);
        Assert.Equal(Iban, q3.Recipient!.Iban);
        Assert.NotNull(q3.QrContent);

        await using var late = AppOn(new DateOnly(2027, 1, 10));
        using var lateOwner = await ApiFixture.SignIn(late, ApiFixture.AllowedEmail);
        var overdue = await Details(lateOwner, Q3Levy);

        Assert.Equal(PaymentAccountExpiryState.Expired, overdue.Expiry!.State);
        Assert.Null(overdue.Recipient);
        Assert.Null(overdue.QrContent);
    }

    [Fact]
    public async Task On_the_last_day_of_an_account_it_is_still_valid()
    {
        await using var app = AppOn(new DateOnly(2026, 12, 31));
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912", new DateOnly(2026, 12, 31));

        var details = await Details(owner, Q4Levy);

        Assert.Equal(PaymentAccountExpiryState.ExpiresBeforeDue, details.Expiry!.State);
        Assert.NotNull(details.Recipient);
    }

    [Fact]
    public async Task A_learned_levy_account_ends_on_its_years_default_until_the_owner_sets_or_removes_the_end()
    {
        await using var app = AppOn(new DateOnly(2027, 1, 10));
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await SeedLearned(ApiFixture.AllowedEmail, PaymentKind.MilitaryLevy, "ГУК Київ", "37993783");

        Assert.Equal(PaymentAccountExpiryState.Expired, (await Details(owner, Q4Levy)).Expiry!.State);

        var ended = await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/MilitaryLevy/valid-until",
            new TreasuryAccountValidUntilRequest(new DateOnly(2026, 12, 31)),
            Json);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal(PaymentAccountExpiryState.Expired, (await Details(owner, Q4Levy)).Expiry!.State);

        await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/MilitaryLevy/valid-until", new TreasuryAccountValidUntilRequest(null), Json);
        Assert.Null((await Details(owner, Q4Levy)).Expiry);
    }

    [Fact]
    public async Task An_end_is_judged_per_kind_so_an_account_of_another_kind_is_unaffected()
    {
        await using var app = AppOn(new DateOnly(2027, 1, 10));
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await PutManual(owner, PaymentKind.MilitaryLevy, "ГУ ДПС у м.Києві", "43141912", new DateOnly(2026, 12, 31));
        await PutManual(owner, PaymentKind.Esv, "ГУ ДПС у м.Києві", "43141912");

        Assert.NotNull((await Details(owner, Q4Levy)).Expiry);
        Assert.Null((await Details(owner, "kind=Esv&periodYear=2026&periodQuarter=4&amountKop=100")).Expiry);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> AppOn(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new Features.Fx.FakeTime(new DateTimeOffset(today, new TimeOnly(10, 0), TimeSpan.Zero)))));

    private async Task<HttpClient> SignInEmpty(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string email)
    {
        var client = await ApiFixture.SignIn(app, email);
        var response = await client.PostAsync("/api/restore", new StringContent(Empty, Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return client;
    }

    // The limit is one minimum wage (8,647.00), so 5,000.00 in Q1 and 5,000.00 in Q2 cross it in Q2 and
    // group 3 ends after Q2.
    private static async Task SetUpCrossedInQ2(HttpClient owner, int year)
    {
        var taxYear = new TaxYearConfigRequest(
            864_700, 500, 100, 2_200, 1_500, 1, [85, 100], 19, 40, 10, 15, 10, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", taxYear, Json)).StatusCode);

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

        foreach (var valueDate in new[] { new DateOnly(year, 2, 10), new DateOnly(year, 5, 10) })
        {
            var income = new TransactionRequest(
                valueDate, 500_000, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", income, Json)).StatusCode);
        }
    }

    private static async Task PutManual(HttpClient client, PaymentKind kind, string name, string code, DateOnly? validUntil = null)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/settings/treasury-accounts/{kind}", new TreasuryAccountRequest(Iban, name, code, validUntil), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedLearned(string email, PaymentKind kind, string name, string? code)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
        database.TreasuryAccounts.Add(new TreasuryAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            LearnedIban = LearnedIban,
            LearnedRecipientName = name,
            LearnedRecipientCode = code,
            LearnedExternalId = "op-1",
            LearnedPaidOn = new DateOnly(2026, 7, 1),
            LearnedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();
    }

    private static string[] QrFields(string content)
    {
        Assert.StartsWith(NbuQr.StartCode, content);
        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(content.AsSpan(NbuQr.StartCode.Length))).Split('\n');
    }

    private static async Task<PaymentDetailsResponse> Details(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/payment-details?{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentDetailsResponse>(Json))!;
    }
}
