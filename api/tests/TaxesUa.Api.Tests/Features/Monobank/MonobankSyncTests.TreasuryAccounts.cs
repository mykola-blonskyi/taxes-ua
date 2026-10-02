using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Monobank;

// Treasury accounts learned from confirmed candidates (#98). The two owners are shared by the whole class,
// and what an account learns depends on every confirmation of its kind, so each test starts its owner with
// no candidates, budget payments or Treasury accounts.
public sealed partial class MonobankSyncTests
{
    private const string OtherTreasuryIban = "UA678999980313191000026007234";

    private const string ThirdTreasuryIban = "UA158999980313200000026007235";

    [Fact]
    public async Task Confirming_a_candidate_records_its_recipient_as_the_learned_account_of_the_kind()
    {
        const int year = 2090;
        var bank = new FakeBank();
        bank.Connect("token-tre-learn", ("tre-learn-uah", 980));
        bank.Put("tre-learn-uah", new Operation("op-tre-1", At(year, 3, 2, 9), -1_500_00, 980,
            CounterName: "ГУК у м.Києві/Печерс.р-н", Comment: "ВЗ", CounterIban: LevyIban2026, CounterEdrpou: "37993783"));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _ownerEmail);
        using var owner = await Connect(app, _ownerEmail, "token-tre-learn");
        await Sync(app, owner);

        var confirmed = await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.MilitaryLevy, year, quarter: 1);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        var account = await TreasuryAccount(owner, PaymentKind.MilitaryLevy);
        Assert.Equal(
            (TreasuryAccountSource.Learned, LevyIban2026, "ГУК у м.Києві/Печерс.р-н", "37993783", true, 0),
            (account.Source, account.Iban, account.RecipientName, account.RecipientCode, account.HasLearned, account.Missing.Length));
        Assert.Equal(("op-tre-1", new DateOnly(year, 3, 2)), (account.Learned!.OperationId, account.Learned.PaidOn));
        Assert.Null(account.Notice);

        bank.Put("tre-learn-uah", new Operation("op-tre-2", At(year, 3, 3, 9), -100_00, 980,
            Comment: "ВЗ", CounterIban: LevyIban2026, CounterName: null, CounterEdrpou: null));
        await Sync(app, owner);
        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.MilitaryLevy, year, quarter: 1);

        account = await TreasuryAccount(owner, PaymentKind.MilitaryLevy);
        Assert.Equal(("op-tre-2", "ГУК у м.Києві/Печерс.р-н", "37993783"), (account.Learned!.OperationId, account.RecipientName, account.RecipientCode));

        bank.Put("tre-learn-uah", new Operation("op-tre-3", At(year, 3, 4, 9), -100_00, 980,
            Comment: "ВЗ", CounterIban: OtherTreasuryIban, CounterName: null, CounterEdrpou: "1234567890"));
        await Sync(app, owner);
        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.MilitaryLevy, year, quarter: 1);

        account = await TreasuryAccount(owner, PaymentKind.MilitaryLevy);
        Assert.Equal((OtherTreasuryIban, null, null), (account.Iban, account.RecipientName, account.RecipientCode));
        Assert.Equal(["recipientName", "recipientCode"], account.Missing);

        var history = await owner.GetFromJsonAsync<JsonElement>("/api/audit?entity=TreasuryAccount", Json);
        Assert.Contains(
            history.EnumerateArray(),
            entry => entry.GetProperty("after").GetProperty("learnedIban").GetString() == OtherTreasuryIban);
    }

    [Fact]
    public async Task A_manual_account_wins_and_a_confirmation_to_another_iban_raises_a_notice_until_dismissed()
    {
        const int year = 2091;
        var bank = new FakeBank();
        bank.Connect("token-tre-manual", ("tre-manual-uah", 980));
        bank.Put("tre-manual-uah", new Operation("op-tre-m1", At(year, 3, 2, 9), -100_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: "ГУК Інше", CounterEdrpou: "37993784"));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _otherEmail);
        using var owner = await Connect(app, _otherEmail, "token-tre-manual");
        var manual = new TreasuryAccountRequest(BudgetIban, "ГУК у м.Києві", "37993783");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/SingleTax", manual, Json)).StatusCode);
        await Sync(app, owner);

        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.SingleTax, year, quarter: 1);

        var account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal((TreasuryAccountSource.Manual, BudgetIban, "ГУК у м.Києві", "37993783"), (account.Source, account.Iban, account.RecipientName, account.RecipientCode));
        Assert.True(account.HasLearned);
        Assert.Equal((OtherTreasuryIban, "ГУК Інше", "37993784"), (account.Notice!.Iban, account.Notice.RecipientName, account.Notice.RecipientCode));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/settings/treasury-accounts/SingleTax/notice/dismiss", null)).StatusCode);
        account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal((TreasuryAccountSource.Manual, BudgetIban), (account.Source, account.Iban));
        Assert.Null(account.Notice);

        bank.Put("tre-manual-uah", new Operation("op-tre-m2", At(year, 3, 3, 9), -100_00, 980, Comment: "ЄП", CounterIban: BudgetIban));
        await Sync(app, owner);
        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.SingleTax, year, quarter: 1);
        Assert.Null((await TreasuryAccount(owner, PaymentKind.SingleTax)).Notice);

        bank.Put("tre-manual-uah", new Operation("op-tre-m3", At(year, 3, 4, 9), -100_00, 980, Comment: "ЄП", CounterIban: ThirdTreasuryIban));
        await Sync(app, owner);
        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.SingleTax, year, quarter: 1);
        Assert.Equal(ThirdTreasuryIban, (await TreasuryAccount(owner, PaymentKind.SingleTax)).Notice!.Iban);

        var reverted = await owner.PostAsync("/api/settings/treasury-accounts/SingleTax/revert", null);
        Assert.Equal(HttpStatusCode.OK, reverted.StatusCode);
        account = (await reverted.Content.ReadFromJsonAsync<TreasuryAccountResponse>(Json))!;
        Assert.Equal((TreasuryAccountSource.Learned, ThirdTreasuryIban, "op-tre-m3"), (account.Source, account.Iban, account.Learned!.OperationId));
        Assert.Null(account.Notice);
    }

    [Fact]
    public async Task A_confirmation_teaches_only_the_kind_it_was_confirmed_for_and_only_its_owner()
    {
        const int year = 2092;
        var bank = new FakeBank();
        bank.Connect("token-tre-iso", ("tre-iso-uah", 980));
        bank.Put("tre-iso-uah", new Operation("op-tre-iso", At(year, 3, 2, 9), -100_00, 980,
            Comment: "ЄСВ", CounterIban: EsvIban, CounterName: "ГУ ДПС", CounterEdrpou: "43141912"));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _ownerEmail);
        using var owner = await Connect(app, _ownerEmail, "token-tre-iso");
        using var stranger = await ApiFixture.SignIn(app.Factory, _otherEmail);
        var others = await Task.WhenAll(
            TreasuryAccount(owner, PaymentKind.SingleTax), TreasuryAccount(stranger, PaymentKind.Esv), TreasuryAccount(stranger, PaymentKind.SingleTax));
        await Sync(app, owner);

        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.Esv, year, quarter: 1);

        Assert.Equal(EsvIban, (await TreasuryAccount(owner, PaymentKind.Esv)).Iban);
        Assert.Equal(others[0].Iban, (await TreasuryAccount(owner, PaymentKind.SingleTax)).Iban);
        Assert.Equal(others[1].Iban, (await TreasuryAccount(stranger, PaymentKind.Esv)).Iban);
        Assert.Equal(others[2].Iban, (await TreasuryAccount(stranger, PaymentKind.SingleTax)).Iban);
    }

    [Fact]
    public async Task The_latest_operation_wins_whatever_order_the_owner_confirms_in()
    {
        const int year = 2093;
        var bank = new FakeBank();
        bank.Connect("token-tre-order", ("tre-order-uah", 980));
        bank.Put("tre-order-uah", new Operation("op-ord-new", At(year, 3, 10, 9), -100_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: null, CounterEdrpou: null));
        bank.Put("tre-order-uah", new Operation("op-ord-other", At(year, 3, 3, 9), -200_00, 980,
            Comment: "ЄП", CounterIban: ThirdTreasuryIban, CounterName: "ГУК Інше", CounterEdrpou: "37993784"));
        bank.Put("tre-order-uah", new Operation("op-ord-same", At(year, 3, 5, 9), -300_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: "ГУК у м.Києві", CounterEdrpou: "37993783"));
        bank.Put("tre-order-uah", new Operation("op-ord-older", At(year, 3, 2, 9), -400_00, 980,
            Comment: "ЄП", CounterIban: ThirdTreasuryIban));
        bank.Put("tre-order-uah", new Operation("op-ord-tie", At(year, 3, 10, 15), -500_00, 980,
            Comment: "ЄП", CounterIban: ThirdTreasuryIban, CounterName: "ГУК Інше", CounterEdrpou: "37993784"));
        await using var app = Create(At(year, 3, 12, 10), bank);
        await ForgetPayments(app, _ownerEmail);
        using var owner = await Connect(app, _ownerEmail, "token-tre-order");

        await ConfirmByAmount(owner, year, 100_00, PaymentKind.SingleTax);
        await ConfirmByAmount(owner, year, 200_00, PaymentKind.SingleTax);

        var account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-ord-new", OtherTreasuryIban, null, null), (account.Learned!.OperationId, account.Iban, account.RecipientName, account.RecipientCode));

        await ConfirmByAmount(owner, year, 300_00, PaymentKind.SingleTax);

        account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-ord-new", OtherTreasuryIban, "ГУК у м.Києві", "37993783"), (account.Learned!.OperationId, account.Iban, account.RecipientName, account.RecipientCode));

        var manual = new TreasuryAccountRequest(OtherTreasuryIban, "ГУК у м.Києві", "37993783");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/SingleTax", manual, Json)).StatusCode);
        await ConfirmByAmount(owner, year, 400_00, PaymentKind.SingleTax);

        Assert.Null((await TreasuryAccount(owner, PaymentKind.SingleTax)).Notice);

        await ConfirmByAmount(owner, year, 500_00, PaymentKind.SingleTax);

        account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-ord-tie", ThirdTreasuryIban), (account.Notice!.OperationId, account.Notice.Iban));
    }

    [Fact]
    public async Task Deleting_or_retyping_the_payment_an_account_learned_from_relearns_it_from_the_latest_confirmation_left()
    {
        const int year = 2094;
        var bank = new FakeBank();
        bank.Connect("token-tre-retract", ("tre-retract-uah", 980));
        bank.Put("tre-retract-uah", new Operation("op-ret-a", At(year, 3, 1, 9), -100_00, 980,
            Comment: "ЄСВ", CounterIban: OtherTreasuryIban, CounterName: "ГУК А", CounterEdrpou: "37993783"));
        bank.Put("tre-retract-uah", new Operation("op-ret-b", At(year, 3, 2, 9), -200_00, 980,
            Comment: "ЄСВ", CounterIban: ThirdTreasuryIban, CounterName: "ГУК Б", CounterEdrpou: "37993784"));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _otherEmail);
        using var owner = await Connect(app, _otherEmail, "token-tre-retract");
        await ConfirmByAmount(owner, year, 100_00, PaymentKind.Esv);
        await ConfirmByAmount(owner, year, 200_00, PaymentKind.Esv);
        Assert.Equal("op-ret-b", (await TreasuryAccount(owner, PaymentKind.Esv)).Learned!.OperationId);
        var payments = (await Payments(owner, year)).ToDictionary(payment => payment.AmountKop);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/payments/{payments[200_00].Id}")).StatusCode);

        var account = await TreasuryAccount(owner, PaymentKind.Esv);
        Assert.Equal(
            ("op-ret-a", OtherTreasuryIban, "ГУК А", "37993783"),
            (account.Learned!.OperationId, account.Iban, account.RecipientName, account.RecipientCode));

        var manual = new TreasuryAccountRequest(BudgetIban, "ГУК у м.Києві", "37993783");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", manual, Json)).StatusCode);
        var retyped = await owner.PutAsJsonAsync(
            $"/api/payments/{payments[100_00].Id}",
            new PaymentRequest(payments[100_00].PaidOn, PaymentKind.MilitaryLevy, 100_00, year, 1, null, null),
            Json);
        Assert.Equal(HttpStatusCode.OK, retyped.StatusCode);

        account = await TreasuryAccount(owner, PaymentKind.Esv);
        Assert.Equal((TreasuryAccountSource.None, false), (account.Source, account.HasLearned));
        account = await TreasuryAccount(owner, PaymentKind.MilitaryLevy);
        Assert.Equal((TreasuryAccountSource.Manual, true, OtherTreasuryIban), (account.Source, account.HasLearned, account.Notice?.Iban));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/payments/{payments[100_00].Id}")).StatusCode);

        account = await TreasuryAccount(owner, PaymentKind.MilitaryLevy);
        Assert.Equal((TreasuryAccountSource.Manual, BudgetIban, false, null), (account.Source, account.Iban, account.HasLearned, account.Notice));
    }

    [Fact]
    public async Task A_sync_fills_a_counterparty_code_its_candidate_lacks_and_the_account_it_taught()
    {
        const int year = 2095;
        var bank = new FakeBank();
        bank.Connect("token-tre-fill", ("tre-fill-uah", 980));
        bank.Put("tre-fill-uah", new Operation("op-tre-fill", At(year, 3, 1, 9), -100_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: "ГУК", CounterEdrpou: "37993783"));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _ownerEmail);
        using var owner = await Connect(app, _ownerEmail, "token-tre-fill");
        await ConfirmByAmount(owner, year, 100_00, PaymentKind.SingleTax);
        var file = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        Assert.Single(file["budgetPaymentCandidates"]!.AsArray())!["counterEdrpou"] = null;
        Assert.Single(file["treasuryAccounts"]!.AsArray())!["learnedRecipientCode"] = null;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        var restore = await owner.PostAsync("/api/restore", new StringContent(file.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        Assert.Equal("recipientCode", Assert.Single((await TreasuryAccount(owner, PaymentKind.SingleTax)).Missing));

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-tre-fill" })).StatusCode);
        await Drain(app, owner);

        var backup = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        Assert.Equal("37993783", Assert.Single(backup["budgetPaymentCandidates"]!.AsArray())!["counterEdrpou"]?.GetValue<string>());
        var account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("37993783", 0), (account.RecipientCode, account.Missing.Length));
    }

    [Fact]
    public async Task Confirming_a_payment_to_another_account_than_the_manual_one_answers_with_the_notice()
    {
        const int year = 2096;
        var bank = new FakeBank();
        bank.Connect("token-tre-inline", ("tre-inline-uah", 980));
        bank.Put("tre-inline-uah", new Operation("op-inl-other", At(year, 3, 1, 9), -100_00, 980, Comment: "ВЗ", CounterIban: OtherTreasuryIban));
        bank.Put("tre-inline-uah", new Operation("op-inl-same", At(year, 3, 2, 9), -200_00, 980, Comment: "ВЗ", CounterIban: BudgetIban));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, _otherEmail);
        using var owner = await Connect(app, _otherEmail, "token-tre-inline");
        var manual = new TreasuryAccountRequest(BudgetIban, "ГУК у м.Києві", "37993783");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", manual, Json)).StatusCode);

        var other = await ConfirmByAmount(owner, year, 100_00, PaymentKind.MilitaryLevy);
        var same = await ConfirmByAmount(owner, year, 200_00, PaymentKind.MilitaryLevy);

        Assert.Equal((BudgetIban, 100_00L), (other.Notice!.ManualIban, other.Payment.AmountKop));
        Assert.Equal((null, 200_00L), (same.Notice, same.Payment.AmountKop));
    }

    private static async Task<ConfirmCandidateResponse> ConfirmByAmount(HttpClient owner, int year, long amountKop, PaymentKind kind)
    {
        var candidate = (await Candidates(owner, year)).Single(row => row.AmountKop == amountKop);
        var response = await ConfirmCandidate(owner, candidate, kind, year, quarter: 1);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ConfirmCandidateResponse>(Json))!;
    }

    private static async Task<TreasuryAccountResponse> TreasuryAccount(HttpClient owner, PaymentKind kind) =>
        (await owner.GetFromJsonAsync<TreasuryAccountResponse[]>("/api/settings/treasury-accounts", Json))!
            .Single(account => account.Kind == kind);

    private static async Task ForgetPayments(SyncApp app, string email)
    {
        using var owner = await ApiFixture.SignIn(app.Factory, email);
        var file = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        file["budgetPayments"] = new JsonArray();
        file["budgetPaymentCandidates"] = new JsonArray();
        file["treasuryAccounts"] = new JsonArray();
        var restore = await owner.PostAsync("/api/restore", new StringContent(file.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
    }
}
