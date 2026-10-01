using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Monobank;

// A Treasury account rebuilt when any confirmation of its IBAN changes, not only the one it learned from (#124).
public sealed partial class MonobankSyncTests
{
    [Fact]
    public async Task Retracting_an_older_confirmation_takes_back_the_name_and_code_it_filled_in()
    {
        const int year = 2098;
        var bank = new FakeBank();
        bank.Connect("token-tre-older", ("tre-older-uah", 980));
        bank.Put("tre-older-uah", new Operation("op-old-a", At(year, 3, 1, 9), -100_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: "ГУК А", CounterEdrpou: "37993783"));
        bank.Put("tre-older-uah", new Operation("op-old-b", At(year, 3, 2, 9), -200_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: null, CounterEdrpou: null));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, ApiFixture.AllowedEmail);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-tre-older");
        await ConfirmByAmount(owner, year, 100_00, PaymentKind.SingleTax);
        await ConfirmByAmount(owner, year, 200_00, PaymentKind.SingleTax);
        var account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-old-b", "ГУК А", "37993783"), (account.Learned!.OperationId, account.RecipientName, account.RecipientCode));
        var payments = (await Payments(owner, year)).ToDictionary(payment => payment.AmountKop);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/payments/{payments[100_00].Id}")).StatusCode);

        account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-old-b", OtherTreasuryIban, null, null), (account.Learned!.OperationId, account.Iban, account.RecipientName, account.RecipientCode));
    }

    [Fact]
    public async Task A_sync_filling_the_code_of_an_older_confirmation_reaches_the_account_learned_from_a_later_one()
    {
        const int year = 2098;
        var bank = new FakeBank();
        bank.Connect("token-tre-late", ("tre-late-uah", 980));
        bank.Put("tre-late-uah", new Operation("op-late-a", At(year, 3, 1, 9), -100_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: "ГУК", CounterEdrpou: "37993783"));
        bank.Put("tre-late-uah", new Operation("op-late-b", At(year, 3, 2, 9), -200_00, 980,
            Comment: "ЄП", CounterIban: OtherTreasuryIban, CounterName: null, CounterEdrpou: null));
        await using var app = Create(At(year, 3, 5, 10), bank);
        await ForgetPayments(app, ApiFixture.SecondAllowedEmail);
        using var owner = await Connect(app, ApiFixture.SecondAllowedEmail, "token-tre-late");
        await ConfirmByAmount(owner, year, 100_00, PaymentKind.SingleTax);
        await ConfirmByAmount(owner, year, 200_00, PaymentKind.SingleTax);
        var file = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        file["budgetPaymentCandidates"]!.AsArray().Single(row => row!["externalId"]!.GetValue<string>() == "op-late-a")!["counterEdrpou"] = null;
        Assert.Single(file["treasuryAccounts"]!.AsArray())!["learnedRecipientCode"] = null;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/monobank/connection")).StatusCode);
        var restore = await owner.PostAsync("/api/restore", new StringContent(file.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.True(restore.StatusCode == HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        var account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-late-b", "recipientCode"), (account.Learned!.OperationId, Assert.Single(account.Missing)));

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "token-tre-late" })).StatusCode);
        await Drain(app, owner);

        account = await TreasuryAccount(owner, PaymentKind.SingleTax);
        Assert.Equal(("op-late-b", "37993783", 0), (account.Learned!.OperationId, account.RecipientCode, account.Missing.Length));
    }
}
