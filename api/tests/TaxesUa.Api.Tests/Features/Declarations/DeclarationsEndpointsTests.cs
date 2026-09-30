using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Tests.Features.Declarations;

// ApiFixture is an IClassFixture, so this class owns its database. xUnit runs one class's tests in
// sequence but in no fixed order, so each test uses a year of its own and first sets the registration
// date and details it needs. A pending payment candidate is the owner's across every test, so the
// one test that adds one resolves it again. The clock is in the 2080s because the test client drops a session
// cookie whose expiry is already past on the real clock.
public sealed class DeclarationsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task The_declaration_carries_the_engines_lines_the_years_rates_and_the_deadlines()
    {
        const int year = 2081;
        await using var application = At(new DateOnly(year, 7, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 1, 20), 12_345_678);
        await PostIncome(owner, new DateOnly(year, 4, 15), 9_876_543);

        var declaration = await Get(owner, year, 2);

        Assert.Equal(
            new DeclarationFiguresResponse(22_222_221, 0, 22_222_221, 0, 1_111_111, 1_111_111, 617_284, 493_827, 222_222, 123_457, 98_765, null),
            declaration.Figures);
        Assert.Equal((500, 100), (declaration.SingleTaxRateBp, declaration.MilitaryLevyRateBp));
        Assert.Equal(
            (new DateOnly(year, 8, 9), new DateOnly(year, 8, 19)),
            (declaration.Filing.Statutory, declaration.Payment.Statutory));
        Assert.Null(declaration.Filed);
    }

    [Fact]
    public async Task Unpaid_obligations_are_a_warning_that_leaves_the_declaration_ready()
    {
        const int year = 2092;
        await using var application = At(new DateOnly(year, 7, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 1, 20), 12_345_678);
        await PostIncome(owner, new DateOnly(year, 4, 15), 9_876_543);
        var payment = await owner.PostAsJsonAsync(
            "/api/payments",
            new PaymentRequest(new DateOnly(year, 5, 15), PaymentKind.SingleTax, 617_284, year, 1, null, null),
            Json);
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);

        var readiness = (await Get(owner, year, 2)).Readiness;

        Assert.Equal(new UnpaidResponse(0, 123_457, 6 * 190_234), readiness.Unpaid);
        Assert.True(readiness.Ready);
    }

    [Fact]
    public async Task Unpaid_obligations_include_what_earlier_years_still_owe_once_it_has_fallen_due()
    {
        const int year = 2094;
        await using var application = At(new DateOnly(year, 7, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await SetUp(owner, year - 1);
        await PostIncome(owner, new DateOnly(year - 1, 10, 10), 1_000_000);

        var readiness = (await Get(owner, year, 1)).Readiness;

        Assert.Equal(50_000, readiness.Unpaid.SingleTaxKop);
        Assert.Equal(10_000, readiness.Unpaid.MilitaryLevyKop);
        Assert.True(readiness.Unpaid.EsvKop >= 12 * 190_234, readiness.Unpaid.EsvKop.ToString());
        Assert.True(readiness.Ready);
    }

    [Fact]
    public async Task Unpaid_obligations_leave_out_what_falls_due_after_the_quarters_filing_deadline()
    {
        const int year = 2095;
        await using var application = At(new DateOnly(year, 7, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 1, 20), 1_000_000);
        await PostIncome(owner, new DateOnly(year, 4, 15), 1_000_000);

        var first = (await Get(owner, year, 1)).Readiness.Unpaid;
        var second = (await Get(owner, year, 2)).Readiness.Unpaid;

        Assert.Equal(0, first.SingleTaxKop);
        Assert.Equal(50_000, second.SingleTaxKop);
    }

    [Fact]
    public async Task A_receipt_to_review_blocks_the_declaration_until_it_is_reviewed()
    {
        const int year = 2082;
        await using var application = At(new DateOnly(year, 5, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        var inQuarter = await PostIncome(owner, new DateOnly(year, 2, 10), 1_000_000);
        var afterQuarter = await PostIncome(owner, new DateOnly(year, 4, 10), 1_000_000);
        await NeedsReview(application, inQuarter.Id, afterQuarter.Id);

        var before = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((1, false), (before.ReceiptsToReview, before.Ready));

        var confirmed = await owner.PostAsJsonAsync(
            $"/api/transactions/{inQuarter.Id}/confirm", new ConfirmRequest(TransactionKind.Income), Json);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        var after = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((0, true), (after.ReceiptsToReview, after.Ready));
        Assert.Equal(1, (await Get(owner, year, 2)).Readiness.ReceiptsToReview);
    }

    [Fact]
    public async Task A_pending_payment_candidate_blocks_only_the_quarter_its_payment_date_falls_in()
    {
        const int year = 2083;
        await using var application = At(new DateOnly(year, 7, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        var candidate = await AddPendingCandidate(application, new DateTimeOffset(year, 4, 15, 9, 0, 0, TimeSpan.Zero));

        var other = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((0, true), (other.PendingPaymentCandidates, other.Ready));
        var later = (await Get(owner, year, 3)).Readiness;
        Assert.Equal((0, true), (later.PendingPaymentCandidates, later.Ready));
        var before = (await Get(owner, year, 2)).Readiness;
        Assert.Equal((1, false), (before.PendingPaymentCandidates, before.Ready));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await owner.PostAsync($"/api/payments/candidates/{candidate}/dismiss", null)).StatusCode);

        var after = (await Get(owner, year, 2)).Readiness;
        Assert.Equal((0, true), (after.PendingPaymentCandidates, after.Ready));
    }

    [Fact]
    public async Task A_candidate_is_placed_by_its_payment_date_in_Kyiv_at_the_quarters_edges()
    {
        const int year = 2093;
        await using var application = At(new DateOnly(year, 7, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        // 21:30 UTC on 31 March is 00:30 on 1 April in Kyiv, so the payment belongs to the second quarter.
        var candidate = await AddPendingCandidate(application, new DateTimeOffset(year, 3, 31, 21, 30, 0, TimeSpan.Zero));

        Assert.Equal(0, (await Get(owner, year, 1)).Readiness.PendingPaymentCandidates);
        Assert.Equal(1, (await Get(owner, year, 2)).Readiness.PendingPaymentCandidates);

        await owner.PostAsync($"/api/payments/candidates/{candidate}/dismiss", null);
    }

    [Fact]
    public async Task An_unverified_tax_year_blocks_the_declaration_until_it_is_verified()
    {
        const int year = 2084;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", TaxYear(), Json)).StatusCode);

        var before = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((false, false), (before.TaxYearVerified, before.Ready));

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/tax-years/{year}/verify", null)).StatusCode);

        var after = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((true, true), (after.TaxYearVerified, after.Ready));
    }

    [Fact]
    public async Task A_missing_registration_date_blocks_the_declaration_and_leaves_nothing_unpaid()
    {
        const int year = 2084;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutSettings(owner, registered: null);

        var before = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((false, new UnpaidResponse(0, 0, 0), false), (before.RegistrationDateSet, before.Unpaid, before.Ready));

        await PutSettings(owner, new DateOnly(year, 1, 1));

        var after = (await Get(owner, year, 1)).Readiness;
        Assert.Equal((true, true), (after.RegistrationDateSet, after.Ready));
    }

    [Fact]
    public async Task Missing_details_block_the_declaration_until_they_are_complete()
    {
        const int year = 2085;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutInvoicing(owner, name: "");
        await PutDetails(owner, new DeclarationDetailsRequest(26, 5, "ГУ ДПС у м. Києві", [], "Київ"));

        var before = (await Get(owner, year, 1)).Readiness;
        Assert.Equal([DeclarationDetailField.Name, DeclarationDetailField.Kved], before.MissingDetails);
        Assert.False(before.Ready);

        await PutInvoicing(owner, name: "ФОП Тест");
        await PutDetails(owner, CompleteDetails);

        var after = (await Get(owner, year, 1)).Readiness;
        Assert.Empty(after.MissingDetails);
        Assert.True(after.Ready);
    }

    [Fact]
    public async Task The_crossing_quarter_declares_its_excess_and_a_later_quarter_has_no_group_3_declaration()
    {
        const int year = 2086;
        await using var application = At(new DateOnly(year, 7, 5));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, incomeLimitMinWages: 1);
        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);

        var under = await Get(owner, year, 1);
        var crossed = await Get(owner, year, 2);
        var after = await Get(owner, year, 3);

        Assert.Equal((false, true, 0L), (under.Readiness.OutsideGroup3, under.Readiness.Ready, under.Figures!.ExcessIncomeKop));
        Assert.Equal(
            new DeclarationFiguresResponse(864_700, 135_300, 1_000_000, 20_295, 43_235, 63_530, 25_000, 38_530, 10_000, 5_000, 5_000, null),
            crossed.Figures);
        Assert.Equal((false, true, 1_500), (crossed.Readiness.OutsideGroup3, crossed.Readiness.Ready, crossed.ExcessRateBp));
        Assert.Equal(new LimitCrossingResponse(year, 2, year, 3, null), crossed.LimitCrossing);
        Assert.Equal((true, false, true), (after.Readiness.OutsideGroup3, after.Readiness.Ready, after.Figures is null));
        Assert.Equal(new LimitCrossingResponse(year, 2, year, 3, null), after.LimitCrossing);
    }

    [Fact]
    public async Task A_filed_mark_is_idempotent_can_be_undone_and_is_logged()
    {
        const int year = 2087;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 2, 10), 1_000_000);
        var mark = new DeclarationFilingRequest(new DateOnly(year, 4, 15), DeclarationType.Reporting);

        var first = await Mark(owner, year, 1, mark);
        var second = await Mark(owner, year, 1, mark);

        var expected = new DeclarationFilingResponse(new DateOnly(year, 4, 15), DeclarationType.Reporting, 1_000_000, false);
        Assert.Equal((expected, expected), (first, second));
        Assert.Equal(expected, (await Get(owner, year, 1)).Filed);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/declarations/{year}/1/filing")).StatusCode);
        Assert.Null((await Get(owner, year, 1)).Filed);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/declarations/{year}/1/filing")).StatusCode);

        var entries = (await owner.GetFromJsonAsync<AuditEntryResponse[]>("/api/audit?entity=DeclarationFiling", Json))!
            .Where(entry => entry.EntityId.EndsWith($"/{year}/1", StringComparison.Ordinal))
            .OrderBy(entry => entry.Id)
            .ToArray();
        Assert.Equal([AuditAction.Create, AuditAction.Delete], entries.Select(entry => entry.Action));
        Assert.Equal("Reporting", entries[0].After!["type"].GetString());
        Assert.Equal(1_000_000, entries[0].After!["filedIncomeKop"].GetInt64());
    }

    [Fact]
    public async Task A_mark_needs_a_date_after_the_quarter_and_not_after_today_and_a_known_type()
    {
        const int year = 2088;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);

        foreach (var filedOn in new[] { new DateOnly(year, 4, 21), new DateOnly(year, 3, 31) })
        {
            var response = await owner.PutAsJsonAsync(
                $"/api/declarations/{year}/1/filing", new DeclarationFilingRequest(filedOn, DeclarationType.Reporting), Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("filedOn", out _));
        }

        foreach (var type in new[] { "\"Final\"", "0", "\"Reporting, Clarifying\"" })
        {
            var response = await owner.PutAsync(
                $"/api/declarations/{year}/1/filing",
                new StringContent($$"""{"filedOn":"{{year}}-04-15","type":{{type}}}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Null((await Get(owner, year, 1)).Filed);
    }

    [Theory]
    [InlineData("/api/declarations/2088/0")]
    [InlineData("/api/declarations/2088/5")]
    [InlineData("/api/declarations/2199/1")]
    public async Task A_quarter_outside_1_to_4_or_a_year_without_its_parameters_is_not_found(string url)
    {
        await using var application = At(new DateOnly(2088, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, 2088);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(url)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync(
                $"{url}/filing", new DeclarationFilingRequest(new DateOnly(2088, 4, 15), DeclarationType.Reporting), Json)).StatusCode);
    }

    [Fact]
    public async Task A_quarter_that_ends_before_the_registration_date_has_no_declaration()
    {
        const int year = 2088;
        await using var application = At(new DateOnly(year, 7, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutSettings(owner, new DateOnly(year, 4, 10));

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/declarations/{year}/1")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync(
                $"/api/declarations/{year}/1/filing",
                new DeclarationFilingRequest(new DateOnly(year, 4, 15), DeclarationType.Reporting),
                Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/declarations/{year}/2")).StatusCode);
    }

    [Fact]
    public async Task A_receipt_added_or_edited_after_marking_flags_the_mark_until_it_is_marked_again()
    {
        const int year = 2089;
        await using var application = At(new DateOnly(year, 5, 1));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 2, 10), 1_000_000);
        await Mark(owner, year, 1, new DeclarationFilingRequest(new DateOnly(year, 4, 20), DeclarationType.Reporting));

        var late = await PostIncome(owner, new DateOnly(year, 3, 15), 500_000);
        Assert.Equal((true, 1_000_000L), Changed(await Get(owner, year, 1)));

        await Mark(owner, year, 1, new DeclarationFilingRequest(new DateOnly(year, 4, 30), DeclarationType.Clarifying));
        await PostIncome(owner, new DateOnly(year, 4, 10), 700_000);
        Assert.Equal((false, 1_500_000L), Changed(await Get(owner, year, 1)));

        var edited = await owner.PutAsJsonAsync(
            $"/api/transactions/{late.Id}", Income(new DateOnly(year, 3, 15), 400_000), Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal((true, 1_500_000L), Changed(await Get(owner, year, 1)));

        static (bool, long) Changed(DeclarationResponse declaration) =>
            (declaration.Filed!.ChangedSinceFiling, declaration.Filed.FiledIncomeKop);
    }

    [Fact]
    public async Task A_second_owner_sees_neither_the_details_nor_the_marks_nor_the_counts_of_the_first()
    {
        const int year = 2090;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(owner, year);
        var receipt = await PostIncome(owner, new DateOnly(year, 2, 10), 1_000_000);
        await NeedsReview(application, receipt.Id);
        await Mark(owner, year, 1, new DeclarationFilingRequest(new DateOnly(year, 4, 15), DeclarationType.Reporting));
        await PutSettings(other, new DateOnly(year, 1, 1));

        var seen = await Get(other, year, 1);

        Assert.Null(seen.Filed);
        Assert.Equal(0, seen.Readiness.ReceiptsToReview);
        Assert.Equal(5, seen.Readiness.MissingDetails.Length);
        Assert.Equal(0, seen.Figures!.IncomeKop);
        Assert.Equal(
            HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/declarations/{year}/1/filing")).StatusCode);
        Assert.NotNull((await Get(owner, year, 1)).Filed);
        Assert.Equal(1, (await Get(owner, year, 1)).Readiness.ReceiptsToReview);
    }

    [Fact]
    public async Task Without_a_session_the_routes_are_unauthorized()
    {
        await using var application = At(new DateOnly(2090, 4, 20));
        using var visitor = ApiFixture.CreateClient(application);

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/declarations/2090/1")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await visitor.PutAsJsonAsync(
                "/api/declarations/2090/1/filing",
                new DeclarationFilingRequest(new DateOnly(2090, 4, 15), DeclarationType.Reporting),
                Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.DeleteAsync("/api/declarations/2090/1/filing")).StatusCode);
    }

    [Fact]
    public async Task The_home_screen_names_the_declaration_due_until_it_is_marked_filed()
    {
        const int year = 2091;
        var today = new DateOnly(year, 4, 5);
        await using var application = At(today);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        var due = (await Get(owner, year, 1)).Filing.Due;

        Assert.Equal(
            new DeclarationDueResponse(year, 1, due, due.DayNumber - today.DayNumber),
            (await Dashboard(owner)).Declaration);

        await Mark(owner, year, 1, new DeclarationFilingRequest(today, DeclarationType.Reporting));
        Assert.Null((await Dashboard(owner)).Declaration);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/declarations/{year}/1/filing")).StatusCode);
        Assert.NotNull((await Dashboard(owner)).Declaration);
    }

    [Theory]
    [InlineData("2091-06-10", null, null)]
    [InlineData("2091-03-20", null, null)]
    [InlineData("2092-01-10", 2091, 4)]
    public async Task The_home_screen_entry_shows_only_inside_the_last_ended_quarters_filing_window(
        string todayIso, int? year, int? quarter)
    {
        await using var setup = At(new DateOnly(2091, 4, 5));
        using (var owner = await ApiFixture.SignIn(setup, ApiFixture.AllowedEmail))
        {
            await SetUp(owner, 2091);
        }

        await using var application = At(DateOnly.Parse(todayIso));
        using var client = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var entry = (await Dashboard(client)).Declaration;

        Assert.Equal((year, quarter), (entry?.Year, entry?.Quarter));
    }

    private WebApplicationFactory<Program> At(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero)))));

    internal static readonly DeclarationDetailsRequest CompleteDetails =
        new(26, 5, "ГУ ДПС у м. Києві", ["62.01"], "Київ, вул. Тестова 1");

    // A verified tax year with the 2026 parameters, the registration on 1 January, and every detail
    // the declaration needs, so each test breaks exactly the one thing it is about.
    internal static async Task SetUp(HttpClient owner, int year, int incomeLimitMinWages = 1_167)
    {
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync($"/api/tax-years/{year}", TaxYear(incomeLimitMinWages), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/tax-years/{year}/verify", null)).StatusCode);
        await PutSettings(owner, new DateOnly(year, 1, 1));
        await PutInvoicing(owner, name: "ФОП Тест");
        await PutDetails(owner, CompleteDetails);
    }

    private static TaxYearConfigRequest TaxYear(int incomeLimitMinWages = 1_167) => new(
        864_700, 500, 100, 2_200, 1_500, incomeLimitMinWages, [85, 100], 19, 40, 10, 15, [], "a test source");

    private static async Task PutSettings(HttpClient owner, DateOnly? registered)
    {
        var request = new SettingsRequest(
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
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);
    }

    private static async Task PutInvoicing(HttpClient owner, string name)
    {
        var request = new InvoicingDetailsRequest(
            name,
            "FOP Test",
            "1234567890",
            "Київ",
            "Kyiv",
            InvoicingDefaults.AcceptanceEn,
            InvoicingDefaults.AcceptanceUk,
            InvoicingDefaults.FeesEn,
            InvoicingDefaults.FeesUk,
            InvoicingDefaults.TaxStatusEn,
            InvoicingDefaults.TaxStatusUk,
            []);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/invoicing", request, Json)).StatusCode);
    }

    internal static async Task PutDetails(HttpClient owner, DeclarationDetailsRequest request) =>
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/declaration", request, Json)).StatusCode);

    private static TransactionRequest Income(DateOnly valueDate, long amountKop) =>
        new(valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);

    internal static async Task<TransactionResponse> PostIncome(HttpClient owner, DateOnly valueDate, long amountKop)
    {
        var response = await owner.PostAsJsonAsync("/api/transactions", Income(valueDate, amountKop), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    // Only a bank import leaves a row to review, and the import needs a whole fake bank; the review
    // state is what the readiness reads, so it is set directly.
    private static async Task NeedsReview(WebApplicationFactory<Program> application, params Guid[] ids)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var row in await database.Transactions.Where(row => ids.Contains(row.Id)).ToListAsync())
        {
            row.ReviewStatus = ReviewStatus.NeedsReview;
        }

        await database.SaveChangesAsync();
    }

    private static async Task<Guid> AddPendingCandidate(WebApplicationFactory<Program> application, DateTimeOffset bankTime)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users
            .Where(user => user.Email == ApiFixture.AllowedEmail)
            .Select(user => user.Id)
            .SingleAsync();
        var account = new BankAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Bank = Bank.Monobank,
            ExternalId = $"declaration-{Guid.NewGuid():N}",
            Name = "UAH",
            CurrencyCode = 980,
            Iban = string.Empty,
            AccountType = "fop",
            IsFop = true,
            IsActive = true,
            CreatedAt = bankTime,
        };
        var candidate = new BudgetPaymentCandidate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BankAccountId = account.Id,
            ExternalId = "op-declaration",
            BankTime = bankTime,
            AmountKop = 190_234,
            CounterIban = "UA358999980333159998000026011",
            Status = CandidateStatus.Pending,
            CreatedAt = bankTime,
        };
        database.BankAccounts.Add(account);
        database.BudgetPaymentCandidates.Add(candidate);
        await database.SaveChangesAsync();
        return candidate.Id;
    }

    private static async Task<DeclarationResponse> Get(HttpClient owner, int year, int quarter)
    {
        var response = await owner.GetAsync($"/api/declarations/{year}/{quarter}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationResponse>(Json))!;
    }

    private static async Task<DeclarationFilingResponse> Mark(
        HttpClient owner, int year, int quarter, DeclarationFilingRequest request)
    {
        var response = await owner.PutAsJsonAsync($"/api/declarations/{year}/{quarter}/filing", request, Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationFilingResponse>(Json))!;
    }

    private static async Task<DashboardResponse> Dashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;
}
