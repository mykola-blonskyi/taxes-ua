using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
using KindYearBalance = TaxesUa.Api.Features.Periods.KindYearBalance;

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

        await Post(client, Body(year, PaymentKind.MilitaryLevy, 10_000, quarter: 4, paidOn: new DateOnly(2025, 12, 10)));

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
    public async Task A_comma_joined_kind_is_rejected()
    {
        // Enum.TryParse ORs a comma-separated list of member names for any enum, so this string
        // happens to equal the single defined value Esv. StrictEnumJsonConverter must reject the
        // string outright rather than silently store that value.
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsync("/api/payments", RawBody(2076, "SingleTax, Esv", 1_000, quarter: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_integer_kind_is_rejected()
    {
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsync("/api/payments", RawBody(2076, "1", 1_000, quarter: 1, kindIsRaw: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_NUL_in_the_note_is_rejected()
    {
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var response = await client.PostAsJsonAsync(
            "/api/payments", Body(2076, PaymentKind.Esv, 1_000, quarter: 1, note: "April\u0000 ESV"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "note");
    }

    [Fact]
    public async Task A_payment_paid_before_the_registration_date_is_flagged_but_still_saved()
    {
        const int year = 2074;
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);
        await SetRegistrationDate(client, new DateOnly(2024, 3, 1));

        var before = await Post(
            client, Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2024, 2, 1)));
        var onOrAfter = await Post(
            client, Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2024, 3, 1)));

        Assert.True(before.BeforeRegistration);
        Assert.False(onOrAfter.BeforeRegistration);

        var listed = await List(client, year);
        var byId = listed.Items.ToDictionary(item => item.Id);
        Assert.True(byId[before.Id].BeforeRegistration);
        Assert.False(byId[onOrAfter.Id].BeforeRegistration);

        var edited = await client.PutAsJsonAsync(
            $"/api/payments/{onOrAfter.Id}",
            Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2024, 2, 15)),
            Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedPayment = await edited.Content.ReadFromJsonAsync<PaymentResponse>(Json);
        Assert.True(editedPayment!.BeforeRegistration);
    }

    [Fact]
    public async Task A_payment_is_not_flagged_when_no_registration_date_is_set()
    {
        const int year = 2075;
        using var client = await SignIn(fixture, ApiFixture.SecondAllowedEmail);
        await SetRegistrationDate(client, null);

        var payment = await Post(
            client, Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2025, 1, 1)));

        Assert.False(payment.BeforeRegistration);
    }

    [Fact]
    public async Task A_payment_is_accepted_up_to_today_in_kyiv_and_refused_after_it()
    {
        const int year = 2090;
        var today = new DateOnly(2070, 6, 15);
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), today);
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var paidToday = await Post(client, Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: today));
        var tomorrow = Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: today.AddDays(1));
        var created = await client.PostAsJsonAsync("/api/payments", tomorrow, Json);
        var edited = await client.PutAsJsonAsync($"/api/payments/{paidToday.Id}", tomorrow, Json);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        await AssertErrorKey(created, "paidOn", "date_in_future");
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        await AssertErrorKey(edited, "paidOn", "date_in_future");
        var listed = Assert.Single((await List(client, year)).Items);
        Assert.Equal(today, listed.PaidOn);
    }

    [Fact]
    public async Task In_the_evening_utc_today_is_already_tomorrows_date_in_kyiv()
    {
        const int year = 2092;
        // 22:30 UTC in June is 01:30 on the 16th in Kyiv.
        var instant = new DateTimeOffset(2070, 6, 15, 22, 30, 0, TimeSpan.Zero);
        await using var application = fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(instant))));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var accepted = await Post(client, Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2070, 6, 16)));
        var refused = await client.PostAsJsonAsync(
            "/api/payments", Body(year, PaymentKind.Esv, 100_000, quarter: 1, paidOn: new DateOnly(2070, 6, 17)), Json);

        Assert.Equal(new DateOnly(2070, 6, 16), accepted.PaidOn);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await AssertErrorKey(refused, "paidOn", "date_in_future");
    }

    [Fact]
    public async Task A_payment_paid_before_its_period_is_accepted_as_an_advance()
    {
        const int year = 2091;
        using var client = await SignIn(fixture, ApiFixture.AllowedEmail);

        var advance = await Post(
            client, Body(year, PaymentKind.Esv, 100_000, quarter: 4, paidOn: new DateOnly(2025, 3, 1)));

        Assert.Equal(new DateOnly(2025, 3, 1), advance.PaidOn);
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
                    obligation.RemainingKop,
                    obligation.DueDate,
                    obligation.Status),
                actual);
        }

        var q1 = periods!.Quarters[0].Obligations!;
        var q2 = periods.Quarters[1].Obligations!;
        var q3 = periods.Quarters[2].Obligations!;

        Assert.Equal((504_000L, 0L, ObligationStatus.Done), (q1.Esv.PaidKop, q1.Esv.RemainingKop, q1.Esv.Status));
        Assert.Equal((504_000L, 0L, ObligationStatus.Done), (q2.Esv.PaidKop, q2.Esv.RemainingKop, q2.Esv.Status));
        Assert.Equal((168_000L, 336_000L, ObligationStatus.Upcoming), (q3.Esv.PaidKop, q3.Esv.RemainingKop, q3.Esv.Status));

        Assert.Equal((600_000L, 0L, ObligationStatus.Done), (q1.SingleTax.PaidKop, q1.SingleTax.RemainingKop, q1.SingleTax.Status));
        Assert.Equal((200_000L, ObligationStatus.Overdue), (q1.MilitaryLevy.RemainingKop, q1.MilitaryLevy.Status));
        Assert.Equal((0L, ObligationStatus.Done), (q2.MilitaryLevy.RemainingKop, q2.MilitaryLevy.Status));

        Assert.NotNull(periods.Balances);
        Assert.Equal(new KindYearBalance(0, 600_000, 600_000, 0, 400_000), periods.Balances.SingleTax);
        Assert.Equal(new KindYearBalance(0, 200_000, 0, 200_000, 0), periods.Balances.MilitaryLevy);
        Assert.Equal(new KindYearBalance(0, 2_016_000, 1_176_000, 840_000, 0), periods.Balances.Esv);
    }

    // Registered 2082-01-01. Q4 2082 and Q1 2083 each take a 100,000.00 receipt: 6,000.00 single tax and
    // 2,000.00 levy apiece. 2082 overpays the single tax by 4,000.00, leaves the levy unpaid and pays
    // ESV in full. A 2,000.00 levy payment named for Q2 2083 settles the older Q4 2082 levy instead.
    // On 2083-06-30 the Q1 2083 payment deadline has passed.
    [Fact]
    public async Task A_years_debt_and_overpayment_carry_into_the_next_year_of_the_same_kind_only()
    {
        var today = new DateOnly(2083, 6, 30);
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), today);
        // The second owner, because the ledger reaches every later year and the first owner's 2080 test
        // would otherwise pool these payments.
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        foreach (var configured in new[] { 2082, 2083 })
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await owner.PutAsJsonAsync($"/api/tax-years/{configured}", TaxYearRequest(), Json)).StatusCode);
        }

        await SetRegistrationDate(owner, new DateOnly(2082, 1, 1));
        await PostIncome(owner, new DateOnly(2082, 11, 1), 10_000_000);
        await PostIncome(owner, new DateOnly(2083, 2, 1), 10_000_000);
        await Post(owner, Body(2082, PaymentKind.SingleTax, 1_000_000, quarter: 4));
        await Post(owner, Body(2082, PaymentKind.Esv, 2_016_000, quarter: 4));
        await Post(owner, Body(2083, PaymentKind.MilitaryLevy, 200_000, quarter: 2));

        var periods = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2083", Json);

        Assert.Equal(new KindYearBalance(0, 600_000, 400_000, 200_000, 0), periods!.Balances!.SingleTax);
        Assert.Equal(new KindYearBalance(0, 200_000, 0, 200_000, 0), periods.Balances.MilitaryLevy);
        Assert.Equal(new KindYearBalance(0, 2_016_000, 0, 2_016_000, 0), periods.Balances.Esv);

        var q1 = periods.Quarters[0].Obligations!;
        Assert.Equal((400_000L, 200_000L, ObligationStatus.Overdue), (q1.SingleTax.PaidKop, q1.SingleTax.RemainingKop, q1.SingleTax.Status));
        Assert.Equal((0L, 200_000L, ObligationStatus.Overdue), (q1.MilitaryLevy.PaidKop, q1.MilitaryLevy.RemainingKop, q1.MilitaryLevy.Status));
        Assert.Equal((0L, 504_000L), (q1.Esv.PaidKop, q1.Esv.RemainingKop));

        var earlier = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2082", Json);
        var levyQ4 = earlier!.Quarters[3].Obligations!.MilitaryLevy;
        Assert.Equal((200_000L, 0L, ObligationStatus.Done), (levyQ4.PaidKop, levyQ4.RemainingKop, levyQ4.Status));
        Assert.Equal(new KindYearBalance(0, 200_000, 200_000, 0, 0), earlier.Balances!.MilitaryLevy);
        Assert.Equal(new KindYearBalance(0, 600_000, 600_000, 0, 0), earlier.Balances.SingleTax);
    }

    // Registered 2087-01-01, with a single-tax payment named for 2086. The ledger starts at the
    // registration year whichever year is viewed, so that payment is credit in neither view.
    [Fact]
    public async Task The_ledger_starts_at_registration_whichever_year_is_viewed()
    {
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), new DateOnly(2087, 12, 31));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await Configure(owner, 2086, 2087);
        await SetRegistrationDate(owner, new DateOnly(2087, 1, 1));
        await PostIncome(owner, new DateOnly(2087, 2, 10), 10_000_000);
        await Post(owner, Body(2086, PaymentKind.SingleTax, 50_000, quarter: 1));

        var before = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2086", Json);
        var registered = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2087", Json);

        Assert.All(before!.Quarters, quarter => Assert.Null(quarter.Obligations));
        Assert.Null(before.Balances);
        Assert.Equal((true, (int?)null, false), (before.Warnings.YearBeforeRegistration, before.Warnings.MissingTaxYear, before.Warnings.FopRegistrationDateNotSet));
        Assert.Equal((false, (int?)null), (registered!.Warnings.YearBeforeRegistration, registered.Warnings.MissingTaxYear));
        Assert.Equal(new KindYearBalance(0, 600_000, 0, 600_000, 0), registered!.Balances!.SingleTax);
    }

    // 2096 and 2098 are configured and 2097 is not. A payment named for 2097 must not become credit,
    // and 2098 cannot be allocated without 2097's accruals.
    [Fact]
    public async Task The_ledger_stops_at_the_first_year_without_a_configuration()
    {
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), new DateOnly(2098, 12, 31));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await Configure(owner, 2096, 2098);
        await SetRegistrationDate(owner, new DateOnly(2096, 1, 1));
        await PostIncome(owner, new DateOnly(2096, 2, 10), 10_000_000);
        await Post(owner, Body(2097, PaymentKind.SingleTax, 600_000, quarter: 1));

        var first = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2096", Json);
        var afterGap = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2098", Json);

        Assert.Equal(new KindYearBalance(0, 600_000, 0, 600_000, 0), first!.Balances!.SingleTax);
        Assert.All(afterGap!.Quarters, quarter => Assert.Null(quarter.Obligations));
        Assert.Null(afterGap.Balances);
        Assert.Equal((false, (int?)2097, false), (afterGap.Warnings.YearBeforeRegistration, afterGap.Warnings.MissingTaxYear, afterGap.Warnings.FopRegistrationDateNotSet));
        Assert.Null(first.Warnings.MissingTaxYear);
    }

    [Fact]
    public async Task An_unconfigured_registration_year_is_named_as_the_missing_year()
    {
        await using var application = fixture.CreateApplication(
            new StubNbuHandler(_ => throw new InvalidOperationException("NBU called")), new DateOnly(2089, 12, 31));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await Configure(owner, 2089);
        await SetRegistrationDate(owner, new DateOnly(2088, 6, 1));

        var periods = await owner.GetFromJsonAsync<PeriodsResponse>("/api/periods/2089", Json);

        Assert.Null(periods!.Balances);
        Assert.Equal(2088, periods.Warnings.MissingTaxYear);
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
        var years = (await YearAccruals.LoadAsync(database, userId, year, CancellationToken.None))!.Ledger;
        var payments = await PaymentsEndpoints.LoadEngineInputAsync(
            database, userId, years[0].Accrual.Year, years[^1].Accrual.Year, CancellationToken.None);

        var ledger = Balances.ForYears(
            [.. years.Select(each => new LedgerYear(each.Accrual, each.Config.ToEngineInput()))],
            years[0].Settings.ToEngineInput(),
            payments,
            today);
        return
        [
            .. ledger.SingleTax.Obligations, .. ledger.MilitaryLevy.Obligations, .. ledger.Esv.Obligations,
        ];
    }

    private static async Task Configure(HttpClient client, params int[] years)
    {
        foreach (var year in years)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PutAsJsonAsync($"/api/tax-years/{year}", TaxYearRequest(), Json)).StatusCode);
        }
    }

    private static PaymentRequest Body(
        int year,
        PaymentKind kind,
        long amountKop,
        int? quarter = null,
        int? month = null,
        string? note = null,
        DateOnly? paidOn = null) =>
        new(paidOn ?? new DateOnly(2025, 4, 15), kind, amountKop, year, quarter, month, note);

    // A raw JSON body, kind as a string the compiler would not let PaymentRequest carry (a comma list
    // or, with kindIsRaw, a bare number), so the strict enum binding can be exercised directly.
    private static StringContent RawBody(
        int year, string kind, long amountKop, int? quarter = null, int? month = null, bool kindIsRaw = false)
    {
        var node = new JsonObject
        {
            ["paidOn"] = new DateOnly(2025, 4, 15).ToString("yyyy-MM-dd"),
            ["kind"] = kindIsRaw ? JsonNode.Parse(kind) : kind,
            ["amountKop"] = amountKop,
            ["periodYear"] = year,
            ["periodQuarter"] = quarter,
            ["periodMonth"] = month,
            ["note"] = null,
        };

        return new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
    }

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
        800_000L, 600, 200, 2100, 1600, 1200, [80, 95], 20, 41, 11, 16, 10, [], "a test source");

    private static async Task AssertErrorKey(HttpResponseMessage response, string key, string? code = null)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (code is null)
        {
            ProblemAssert.Rejects(problem.RootElement, key);
        }
        else
        {
            ProblemAssert.FieldIs(problem.RootElement, key, code);
        }
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
