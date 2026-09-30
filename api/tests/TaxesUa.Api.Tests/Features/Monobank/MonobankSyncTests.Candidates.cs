using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Tests.Features.Monobank;

// Budget payment candidates (#80). Same shared owners as the rest of the class, so each test keeps its
// own accounts and year, and counts candidates and the review warning as changes rather than totals.
public sealed partial class MonobankSyncTests
{
    private const string BudgetIban = "UA358999980333159998000026011";

    private const string LevyIban2026 = "UA148999980313181000026007233";

    private const string EsvIban = "UA538999980000355689990000001";

    // What the owner confirms for an account is suggested for every later payment to it, so the test of
    // that learning pays an account no other test of these shared owners confirms.
    private const string LearnedIban = "UA048999980313000000026001234";

    // Each test that confirms or edits a payment pays an account of its own: what the owner confirms for
    // an account is suggested for every later payment to it.
    private const string DeletedIban = "UA508999980313111111026001235";

    private const string EditedIban = "UA518999980313111111026001236";

    private const string ShopIban = "UA753220010000026001234567891";

    [Fact]
    public async Task Only_a_settled_hryvnia_debit_to_the_Treasury_becomes_a_candidate()
    {
        var bank = new FakeBank();
        bank.Connect("token-cand", ("cand-uah", 980), ("cand-usd", 840));
        await using var app = Create(At(2062, 3, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand");
        var waiting = await NeedsReviewCount(owner);
        bank.Put("cand-uah", new Operation("op-cand-levy", At(2062, 3, 2, 9), -1_500_00, 980,
            CounterName: "ГУК у м.Києві/Печерс.р-н/11011800", Comment: "Сплата військового збору", CounterIban: LevyIban2026));
        bank.Put("cand-uah", new Operation("op-cand-shop", At(2062, 3, 2, 10), -200_00, 980, CounterIban: ShopIban));
        bank.Put("cand-uah", new Operation("op-cand-held", At(2062, 3, 3, 9), -300_00, 980, Hold: true, CounterIban: BudgetIban));
        bank.Put("cand-uah", new Operation("op-cand-back", At(2062, 3, 3, 10), 100_00, 980, CounterIban: BudgetIban));
        bank.Put("cand-usd", new Operation("op-cand-usd", At(2062, 3, 3, 11), -10_00, 840, CounterIban: BudgetIban));

        await Sync(app, owner);

        var candidate = Assert.Single(await Candidates(owner, 2062));
        Assert.Equal(
            (new DateOnly(2062, 3, 2), 1_500_00L, LevyIban2026, (PaymentKind?)PaymentKind.MilitaryLevy),
            (candidate.PaidOn, candidate.AmountKop, candidate.CounterIban, candidate.SuggestedKind));
        Assert.Equal("ГУК у м.Києві/Печерс.р-н/11011800", candidate.CounterName);
        Assert.Equal("Сплата військового збору", candidate.Purpose);
        Assert.Empty(candidate.Matches);
        Assert.Equal(100_00, Assert.Single((await List(owner, 2062)).Items).AmountMinor);
        Assert.Equal(waiting + 2, await NeedsReviewCount(owner));
        Assert.Equal((2, 0), Counts(await Status(owner), "cand-uah"));

        await Sync(app, owner);
        Assert.Single(await Candidates(owner, 2062));
        Assert.Equal((0, 0), Counts(await Status(owner), "cand-uah"));

        bank.Put("cand-uah", new Operation("op-cand-held", At(2062, 3, 3, 9), -300_00, 980, CounterIban: BudgetIban));
        await Sync(app, owner);
        Assert.Equal([1_500_00L, 300_00L], (await Candidates(owner, 2062)).Select(row => row.AmountKop));
    }

    [Fact]
    public async Task Confirming_records_the_payment_moves_the_next_step_is_never_offered_again_and_teaches_the_kind()
    {
        const int year = 2063;
        var bank = new FakeBank();
        bank.Connect("token-cand-confirm", ("cand-confirm-uah", 980));
        await using var app = Create(At(year, 5, 1, 10), bank);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-cand-confirm");
        await RegisterOn(owner, year);
        var owed = Assert.Single((await Dashboard(owner)).NextStep.Now);
        Assert.Equal((PaymentKind.Esv, 1, ObligationStatus.Overdue), (owed.Kind, owed.FromQuarter, owed.Status));
        bank.Put("cand-confirm-uah", new Operation("op-cand-esv", At(year, 4, 28, 9), -owed.AmountKop, 980,
            CounterName: "ГУ ДПС у м.Києві", Comment: "Оплата", CounterIban: LearnedIban));

        await Sync(app, owner);
        var candidate = Assert.Single(await Candidates(owner, year));
        Assert.Null(candidate.SuggestedKind);
        var waiting = await NeedsReviewCount(owner);

        var confirmed = await ConfirmCandidate(owner, candidate, PaymentKind.Esv, year, quarter: 1);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var payment = (await confirmed.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
        Assert.Equal(
            (new DateOnly(year, 4, 28), PaymentKind.Esv, owed.AmountKop, year, (int?)1),
            (payment.PaidOn, payment.Kind, payment.AmountKop, payment.PeriodYear, payment.PeriodQuarter));
        Assert.Equal(payment.Id, Assert.Single(await Payments(owner, year)).Id);
        Assert.Empty(await Candidates(owner, year));
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));
        var next = Assert.Single((await Dashboard(owner)).NextStep.Now);
        Assert.Equal((PaymentKind.Esv, 2, ObligationStatus.Upcoming), (next.Kind, next.FromQuarter, next.Status));

        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?entity=BudgetPayment&id={payment.Id}", Json);
        var create = Assert.Single(history.EnumerateArray());
        Assert.Equal("op-cand-esv", create.GetProperty("after").GetProperty("externalId").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmCandidate(owner, candidate, PaymentKind.Esv, year, quarter: 1)).StatusCode);
        await Sync(app, owner);
        Assert.Empty(await Candidates(owner, year));
        Assert.Single(await Payments(owner, year));

        bank.Put("cand-confirm-uah", new Operation("op-cand-esv-2", At(year, 4, 30, 9), -1_000_00, 980,
            Comment: "Єдиний податок", CounterIban: LearnedIban));
        await Sync(app, owner);
        Assert.Equal(PaymentKind.Esv, Assert.Single(await Candidates(owner, year)).SuggestedKind);
    }

    [Fact]
    public async Task A_payment_typed_by_hand_is_offered_and_linked_instead_of_recorded_twice()
    {
        const int year = 2064;
        var bank = new FakeBank();
        bank.Connect("token-cand-match", ("cand-match-uah", 980));
        await using var app = Create(At(year, 2, 12, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand-match");
        var typed = await PostPayment(owner, new PaymentRequest(new DateOnly(year, 2, 10), PaymentKind.SingleTax, 3_000_00, year - 1, 4, null, "typed"));
        bank.Put("cand-match-uah", new Operation("op-cand-match", At(year, 2, 10, 9), -3_000_00, 980,
            Comment: "*;101;1234567890;Єдиний податок;;;", CounterIban: BudgetIban));

        await Sync(app, owner);
        var candidate = Assert.Single(await Candidates(owner, year));
        Assert.Equal(PaymentKind.SingleTax, candidate.SuggestedKind);
        Assert.Equal((typed.Id, PaymentKind.SingleTax), (Assert.Single(candidate.Matches).Id, candidate.Matches[0].Kind));

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmCandidate(owner, candidate, PaymentKind.SingleTax, year, quarter: 1)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await ConfirmCandidate(owner, candidate, PaymentKind.MilitaryLevy, year, quarter: 1, link: typed.Id)).StatusCode);
        Assert.Single(await Candidates(owner, year));

        var linked = await ConfirmCandidate(owner, candidate, PaymentKind.SingleTax, year, quarter: 1, link: typed.Id);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(typed, (await linked.Content.ReadFromJsonAsync<PaymentResponse>(Json))!);
        Assert.Equal(typed, Assert.Single(await Payments(owner, year - 1)));
        Assert.Empty(await Payments(owner, year));
        Assert.Empty(await Candidates(owner, year));

        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?entity=BudgetPayment&id={typed.Id}", Json);
        var link = history.EnumerateArray().Single(entry => entry.GetProperty("action").GetString() == "Update");
        Assert.Equal("op-cand-match", link.GetProperty("after").GetProperty("externalId").GetString());
    }

    [Fact]
    public async Task Confirming_over_a_payment_typed_since_the_list_was_read_is_refused_and_records_nothing()
    {
        const int year = 2065;
        var bank = new FakeBank();
        bank.Connect("token-cand-stale", ("cand-stale-uah", 980));
        bank.Put("cand-stale-uah", new Operation("op-cand-stale", At(year, 3, 5, 9), -500_00, 980,
            Comment: "ВЗ", CounterIban: BudgetIban));
        await using var app = Create(At(year, 3, 6, 10), bank);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-cand-stale");
        var shown = Assert.Single(await Candidates(owner, year));
        Assert.Empty(shown.Matches);

        var typed = await PostPayment(owner, new PaymentRequest(new DateOnly(year, 3, 5), PaymentKind.MilitaryLevy, 500_00, year, 1, null, null));
        var refused = await ConfirmCandidate(owner, shown, PaymentKind.MilitaryLevy, year, quarter: 1);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(typed.Id, Assert.Single(await Payments(owner, year)).Id);
        Assert.Equal(typed.Id, Assert.Single(Assert.Single(await Candidates(owner, year)).Matches).Id);
    }

    [Fact]
    public async Task A_dismissed_candidate_never_comes_back_and_cannot_be_confirmed()
    {
        const int year = 2066;
        var bank = new FakeBank();
        bank.Connect("token-cand-dismiss", ("cand-dismiss-uah", 980));
        bank.Put("cand-dismiss-uah", new Operation("op-cand-dismiss", At(year, 6, 1, 9), -700_00, 980,
            Comment: "Повернення помилково сплачених коштів", CounterIban: EsvIban));
        await using var app = Create(At(year, 6, 3, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand-dismiss");
        var candidate = Assert.Single(await Candidates(owner, year));
        Assert.Equal(PaymentKind.Esv, candidate.SuggestedKind);
        var waiting = await NeedsReviewCount(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await Dismiss(owner, candidate)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Dismiss(owner, candidate)).StatusCode);
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmCandidate(owner, candidate, PaymentKind.Esv, year, quarter: 2)).StatusCode);

        await Sync(app, owner);
        app.Clock.Advance(TimeSpan.FromDays(2));
        await Sync(app, owner);

        Assert.Empty(await Candidates(owner, year));
        Assert.Empty(await Payments(owner, year));
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));
    }

    [Fact]
    public async Task Another_owner_can_neither_see_nor_confirm_nor_dismiss_a_candidate()
    {
        const int year = 2067;
        var bank = new FakeBank();
        bank.Connect("token-cand-iso", ("cand-iso-uah", 980));
        bank.Put("cand-iso-uah", new Operation("op-cand-iso", At(year, 1, 20, 9), -900_00, 980,
            Comment: "ЄСВ", CounterIban: BudgetIban));
        await using var app = Create(At(year, 1, 21, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand-iso");
        var candidate = Assert.Single(await Candidates(owner, year));
        using var stranger = await ApiFixture.SignIn(app.Factory, ApiFixture.SecondAllowedEmail);

        Assert.Empty(await Candidates(stranger, year));
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmCandidate(stranger, candidate, PaymentKind.Esv, year, quarter: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Dismiss(stranger, candidate)).StatusCode);

        Assert.Single(await Candidates(owner, year));
        Assert.Empty(await Payments(stranger, year));
    }

    // It restores the owner's whole backup, which rejects a transaction dated after this test's today, so
    // the owner starts from a ledger without transactions, as the tombstone test does.
    [Fact]
    public async Task A_backup_carries_candidates_and_linked_payments_so_a_restore_then_sync_offers_nothing_again()
    {
        const int year = 2068;
        var bank = new FakeBank();
        bank.Connect("token-cand-backup", ("cand-backup-uah", 980));
        bank.Put("cand-backup-uah", new Operation("op-cand-kept", At(year, 4, 1, 9), -100_00, 980, Comment: "ЄП", CounterIban: BudgetIban));
        bank.Put("cand-backup-uah", new Operation("op-cand-gone", At(year, 4, 2, 9), -200_00, 980, Comment: "ЄП", CounterIban: BudgetIban));
        bank.Put("cand-backup-uah", new Operation("op-cand-open", At(year, 4, 3, 9), -300_00, 980, Comment: "Оплата", CounterIban: EsvIban));
        await using var app = Create(At(year, 4, 5, 10), bank);
        await EmptyLedger(app, ApiFixture.SecondAllowedEmail);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-cand-backup");
        var candidates = (await Candidates(owner, year)).ToDictionary(row => row.AmountKop);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmCandidate(owner, candidates[100_00], PaymentKind.SingleTax, year, quarter: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Dismiss(owner, candidates[200_00])).StatusCode);

        var file = await owner.GetStringAsync("/api/backup");
        var document = Parse(file);
        Assert.Equal(
            [("op-cand-kept", "Confirmed", "SingleTax"), ("op-cand-gone", "Dismissed", null), ("op-cand-open", "Pending", null)],
            document.GetProperty("budgetPaymentCandidates").EnumerateArray()
                .Where(row => row.GetProperty("externalId").GetString()!.StartsWith("op-cand-", StringComparison.Ordinal)
                    && row.GetProperty("bankTime").GetDateTimeOffset().Year == year)
                .Select(row => (
                    row.GetProperty("externalId").GetString(),
                    row.GetProperty("status").GetString(),
                    row.GetProperty("confirmedKind").GetString())));
        var payment = document.GetProperty("budgetPayments").EnumerateArray()
            .Single(row => row.GetProperty("paidOn").GetString() == $"{year}-04-01");
        Assert.Equal("op-cand-kept", payment.GetProperty("externalId").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        var restore = await owner.PostAsync("/api/restore", new StringContent(file, System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        Assert.Equal(file, await owner.GetStringAsync("/api/backup"));
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-cand-backup" })).StatusCode);
        await Drain(app, owner);

        Assert.Equal(300_00, Assert.Single(await Candidates(owner, year)).AmountKop);
        Assert.Equal(100_00, Assert.Single(await Payments(owner, year)).AmountKop);
        Assert.Equal((0, 0), Counts(await Status(owner), "cand-backup-uah"));
    }

    [Fact]
    public async Task Deleting_a_payment_confirmed_from_a_candidate_offers_the_operation_again_and_forgets_the_confirmation()
    {
        const int year = 2069;
        var bank = new FakeBank();
        bank.Connect("token-cand-delete", ("cand-delete-uah", 980));
        bank.Put("cand-delete-uah", new Operation("op-cand-del-1", At(year, 4, 1, 9), -100_00, 980, Comment: "Єдиний податок", CounterIban: DeletedIban));
        bank.Put("cand-delete-uah", new Operation("op-cand-del-2", At(year, 4, 2, 9), -200_00, 980, Comment: "Оплата", CounterIban: DeletedIban));
        bank.Put("cand-delete-uah", new Operation("op-cand-del-3", At(year, 4, 3, 9), -300_00, 980, Comment: "Оплата", CounterIban: BudgetIban));
        await using var app = Create(At(year, 4, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand-delete");
        var candidates = (await Candidates(owner, year)).ToDictionary(row => row.AmountKop);
        var waiting = await NeedsReviewCount(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await Dismiss(owner, candidates[300_00])).StatusCode);

        var confirmed = await ConfirmCandidate(owner, candidates[100_00], PaymentKind.Esv, year, quarter: 1);
        var payment = (await confirmed.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
        Assert.Equal(PaymentKind.Esv, Assert.Single(await Candidates(owner, year)).SuggestedKind);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/payments/{payment.Id}")).StatusCode);

        var offered = (await Candidates(owner, year)).ToDictionary(row => row.AmountKop);
        Assert.Equal([100_00L, 200_00L], offered.Keys.Order());
        Assert.Equal(PaymentKind.SingleTax, offered[100_00].SuggestedKind);
        Assert.Null(offered[200_00].SuggestedKind);
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));
        Assert.Empty(await Payments(owner, year));
        await Sync(app, owner);
        Assert.Equal(2, (await Candidates(owner, year)).Length);
    }

    [Fact]
    public async Task Changing_the_kind_of_a_payment_confirmed_from_a_candidate_changes_what_its_account_is_suggested()
    {
        const int year = 2070;
        var bank = new FakeBank();
        bank.Connect("token-cand-edit", ("cand-edit-uah", 980));
        bank.Put("cand-edit-uah", new Operation("op-cand-edit-1", At(year, 4, 1, 9), -100_00, 980, CounterIban: EditedIban));
        bank.Put("cand-edit-uah", new Operation("op-cand-edit-2", At(year, 4, 2, 9), -200_00, 980, Comment: "Оплата", CounterIban: EditedIban));
        await using var app = Create(At(year, 4, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-cand-edit");
        var candidates = (await Candidates(owner, year)).ToDictionary(row => row.AmountKop);

        var confirmed = await ConfirmCandidate(owner, candidates[100_00], PaymentKind.Esv, year, quarter: 1);
        var payment = (await confirmed.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
        Assert.Equal(PaymentKind.Esv, Assert.Single(await Candidates(owner, year)).SuggestedKind);

        var edited = await owner.PutAsJsonAsync(
            $"/api/payments/{payment.Id}",
            new PaymentRequest(payment.PaidOn, PaymentKind.MilitaryLevy, payment.AmountKop, year, 1, null, null),
            Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Equal(PaymentKind.MilitaryLevy, Assert.Single(await Candidates(owner, year)).SuggestedKind);
    }

    [Fact]
    public async Task A_payment_can_be_recorded_separately_from_one_typed_by_hand_with_the_same_date_kind_and_amount()
    {
        const int year = 2071;
        var bank = new FakeBank();
        bank.Connect("token-cand-separate", ("cand-separate-uah", 980));
        bank.Put("cand-separate-uah", new Operation("op-cand-separate", At(year, 2, 10, 9), -3_000_00, 980, Comment: "ЄП", CounterIban: BudgetIban));
        await using var app = Create(At(year, 2, 12, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-cand-separate");
        var typed = await PostPayment(owner, new PaymentRequest(new DateOnly(year, 2, 10), PaymentKind.SingleTax, 3_000_00, year, 1, null, "typed"));
        await Sync(app, owner);
        var candidate = Assert.Single(await Candidates(owner, year));

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmCandidate(owner, candidate, PaymentKind.SingleTax, year, quarter: 1)).StatusCode);
        Assert.Single(await Payments(owner, year));

        var separate = await ConfirmCandidate(owner, candidate, PaymentKind.SingleTax, year, quarter: 1, recordSeparately: true);

        Assert.Equal(HttpStatusCode.OK, separate.StatusCode);
        var recorded = (await separate.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
        Assert.NotEqual(typed.Id, recorded.Id);
        Assert.Equal(new[] { typed.Id, recorded.Id }.Order(), (await Payments(owner, year)).Select(row => row.Id).Order());
        Assert.Empty(await Candidates(owner, year));
    }

    [Fact]
    public async Task A_sync_that_finds_only_a_candidate_reports_it_as_imported()
    {
        var bank = new FakeBank();
        bank.Connect("token-cand-count", ("cand-count-uah", 980));
        await using var app = Create(At(2072, 3, 5, 10), bank);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-cand-count");
        bank.Put("cand-count-uah", new Operation("op-cand-count", At(2072, 3, 2, 9), -100_00, 980, CounterIban: BudgetIban));

        await Sync(app, owner);

        Assert.Equal((1, 0), Counts(await Status(owner), "cand-count-uah"));
    }

    private static async Task RegisterOn(HttpClient owner, int year)
    {
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", TaxYear(), Json)).StatusCode);
        var settings = new SettingsRequest(
            new DateOnly(year, 1, 1), PaymentMode.Quarterly, EsvRegistrationMonthPolicy.FullMonth, false, true, true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday], "uk", "system", "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", settings, Json)).StatusCode);
    }

    private static async Task<PaymentCandidateResponse[]> Candidates(HttpClient owner, int year) =>
        [.. (await owner.GetFromJsonAsync<PaymentCandidateResponse[]>("/api/payments/candidates", Json))!
            .Where(row => row.PaidOn.Year == year)
            .OrderBy(row => row.PaidOn)];

    private static async Task<PaymentResponse[]> Payments(HttpClient owner, int year) =>
        (await owner.GetFromJsonAsync<PaymentListResponse>($"/api/payments?year={year}", Json))!.Items;

    private static async Task<PaymentResponse> PostPayment(HttpClient owner, PaymentRequest request)
    {
        var response = await owner.PostAsJsonAsync("/api/payments", request, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>(Json))!;
    }

    private static Task<HttpResponseMessage> ConfirmCandidate(
        HttpClient owner, PaymentCandidateResponse candidate, PaymentKind kind, int year, int quarter, Guid? link = null, bool recordSeparately = false) =>
        owner.PostAsJsonAsync(
            $"/api/payments/candidates/{candidate.Id}/confirm",
            new ConfirmCandidateRequest(kind, year, quarter, null, link, recordSeparately),
            Json);

    private static Task<HttpResponseMessage> Dismiss(HttpClient owner, PaymentCandidateResponse candidate) =>
        owner.PostAsync($"/api/payments/candidates/{candidate.Id}/dismiss", null);

    private static async Task<DashboardResponse> Dashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;
}
