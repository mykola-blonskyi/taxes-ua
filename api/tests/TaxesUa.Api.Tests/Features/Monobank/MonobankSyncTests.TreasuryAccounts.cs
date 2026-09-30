using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Monobank;

// Treasury accounts learned from confirmed candidates (#98). The two owners are shared by the whole class
// and an account is one row per owner and kind, so each test starts by putting its kind back to Learned.
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
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-tre-learn");
        await BackToLearned(owner, PaymentKind.MilitaryLevy);
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
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-tre-manual");
        await BackToLearned(owner, PaymentKind.SingleTax);
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
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-tre-iso");
        using var stranger = await ApiFixture.SignIn(app.Factory, ApiFixture.SecondAllowedEmail);
        await BackToLearned(owner, PaymentKind.Esv);
        var others = await Task.WhenAll(
            TreasuryAccount(owner, PaymentKind.SingleTax), TreasuryAccount(stranger, PaymentKind.Esv), TreasuryAccount(stranger, PaymentKind.SingleTax));
        await Sync(app, owner);

        await ConfirmCandidate(owner, Assert.Single(await Candidates(owner, year)), PaymentKind.Esv, year, quarter: 1);

        Assert.Equal(EsvIban, (await TreasuryAccount(owner, PaymentKind.Esv)).Iban);
        Assert.Equal(others[0].Iban, (await TreasuryAccount(owner, PaymentKind.SingleTax)).Iban);
        Assert.Equal(others[1].Iban, (await TreasuryAccount(stranger, PaymentKind.Esv)).Iban);
        Assert.Equal(others[2].Iban, (await TreasuryAccount(stranger, PaymentKind.SingleTax)).Iban);
    }

    private static async Task<TreasuryAccountResponse> TreasuryAccount(HttpClient owner, PaymentKind kind) =>
        (await owner.GetFromJsonAsync<TreasuryAccountResponse[]>("/api/settings/treasury-accounts", Json))!
            .Single(account => account.Kind == kind);

    // Earlier tests of the shared owners may have left a Manual account; those always have a learned one too.
    private static async Task BackToLearned(HttpClient owner, PaymentKind kind)
    {
        var account = await TreasuryAccount(owner, kind);
        if (account.Source == TreasuryAccountSource.Manual)
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/settings/treasury-accounts/{kind}/revert", null)).StatusCode);
        }
    }
}
