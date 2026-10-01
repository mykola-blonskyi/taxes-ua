using System.Net;
using System.Net.Http.Json;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Invoices;

namespace TaxesUa.Api.Tests.Features.Monobank;

// An imported receipt paying an invoice (#93).
public sealed partial class MonobankSyncTests
{
    [Fact]
    public async Task Linking_an_import_reviews_it_and_dismissing_it_unlinks_it_so_the_invoice_reopens()
    {
        const int year = 2031;
        var bank = new FakeBank();
        bank.Connect("token-invoice", ("inv-uah", 980));
        bank.Put("inv-uah", new Operation("op-invoice", At(year, 6, 1, 9), 500_00, 980, CounterName: "Invoice Buyer"));
        await using var app = Create(At(year, 6, 10, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-invoice");
        await InvoicesEndpointsTests.SaveDetails(owner);
        var client = (await owner.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json))!.Single(row => row.Name == "Invoice Buyer");
        var completed = await owner.PutAsJsonAsync($"/api/clients/{client.Id}", InvoicesEndpointsTests.ClientBody("Invoice Buyer"), Json);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var draft = await InvoicesEndpointsTests.CreateDraft(
            owner,
            client.Id,
            new DateOnly(year, 6, 1),
            [InvoicesEndpointsTests.Line("Design", "Дизайн", InvoiceUnit.Service, 1_000, 500_00)],
            Currency.UAH);
        var invoice = await InvoicesEndpointsTests.Issue(owner, draft.Id);
        var imported = (await List(owner, year)).Items.Single(row => row.Source is not null);
        Assert.Equal(ReviewStatus.NeedsReview, imported.ReviewStatus);
        var waiting = await NeedsReviewCount(owner);

        var linked = await owner.PostAsync($"/api/invoices/{invoice.Id}/receipts/{imported.Id}", null);
        Assert.True(linked.StatusCode == HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        Assert.Equal(InvoiceStanding.Paid, (await Invoice(owner, invoice.Id)).Standing);
        Assert.Equal(ReviewStatus.Confirmed, (await List(owner, year)).Items.Single(row => row.Id == imported.Id).ReviewStatus);
        Assert.Equal(waiting - 1, await NeedsReviewCount(owner));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{imported.Id}")).StatusCode);
        var reopened = await Invoice(owner, invoice.Id);
        Assert.Equal((InvoiceStanding.Issued, (long?)500_00), (reopened.Standing, reopened.DueMinor));
        Assert.Empty(reopened.Receipts);

        await Sync(app, owner);
        Assert.DoesNotContain((await List(owner, year)).Items, row => row.Source is not null);
        Assert.Empty((await Invoice(owner, invoice.Id)).Receipts);
    }

    [Fact]
    public async Task An_import_matching_an_open_invoice_is_offered_after_a_sync_and_only_confirming_links_it()
    {
        const int year = 2027;
        var bank = new FakeBank();
        bank.Connect("token-suggest-pay", ("sugpay-usd", 840), ("sugpay-uah", 980));
        bank.Put("sugpay-usd", new Operation("op-sp-number", At(year, 6, 2, 9), 800_00, 840,
            CounterName: "Wire Sender", Comment: "Payment of invoice 2027-001"));
        bank.Put("sugpay-usd", new Operation("op-sp-client", At(year, 6, 3, 9), 300_00, 840, CounterName: "Zeta Suggest Ltd"));
        bank.Put("sugpay-usd", new Operation("op-sp-amount", At(year, 6, 4, 9), 301_00, 840, CounterName: "Zeta Suggest Ltd"));
        bank.Put("sugpay-uah", new Operation("op-sp-currency", At(year, 6, 5, 9), 300_00, 980, CounterName: "Zeta Suggest Ltd"));
        await using var app = Create(
            At(year, 6, 10, 10),
            bank,
            Nbu(("USD", new DateOnly(year, 6, 2), "40.0000"), ("USD", new DateOnly(year, 6, 3), "40.0000"), ("USD", new DateOnly(year, 6, 4), "40.0000")));
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-suggest-pay");
        await InvoicesEndpointsTests.SaveDetails(owner);
        var buyer = await CompleteImportedClient(owner, "Zeta Suggest Ltd");
        var other = await InvoicesEndpointsTests.CreateClient(owner, "Unrelated Buyer");
        var byNumber = await IssueFor(owner, other.Id, year, 5, 800_00);
        Assert.Equal($"{year}-001", byNumber.Number);
        var byClientLater = await IssueFor(owner, buyer.Id, year, 20, 300_00);
        var byClientSooner = await IssueFor(owner, buyer.Id, year, 6, 300_00);

        await Sync(app, owner);

        var rows = (await List(owner, year)).Items;
        var number = rows.Single(row => row.Description?.Contains("2027-001") == true);
        var client = rows.Single(row => row.AmountMinor == 300_00 && row.Currency == Currency.USD);
        var wrongAmount = rows.Single(row => row.AmountMinor == 301_00);
        var wrongCurrency = rows.Single(row => row.Currency == Currency.UAH && row.Source is not null);
        var offered = await Suggestions(owner);
        Assert.Equal([byNumber.Id], offered[number.Id]);
        Assert.Equal([byClientSooner.Id, byClientLater.Id], offered[client.Id]);
        Assert.DoesNotContain(wrongAmount.Id, offered.Keys);
        Assert.DoesNotContain(wrongCurrency.Id, offered.Keys);
        Assert.All(rows, row => Assert.Null(row.InvoiceId));
        Assert.Equal(InvoiceStanding.Issued, (await Invoice(owner, byNumber.Id)).Standing);

        var linked = await owner.PostAsync($"/api/invoices/{byNumber.Id}/receipts/{number.Id}", null);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(InvoiceStanding.Paid, (await Invoice(owner, byNumber.Id)).Standing);
        Assert.Equal(byNumber.Number, (await List(owner, year)).Items.Single(row => row.Id == number.Id).InvoiceNumber);
        Assert.DoesNotContain(number.Id, (await Suggestions(owner)).Keys);

        await Sync(app, owner);
        Assert.Equal([byClientSooner.Id, byClientLater.Id], (await Suggestions(owner))[client.Id]);
    }

    [Fact]
    public async Task Confirming_the_import_without_the_suggested_invoice_keeps_it_unlinked_and_ends_the_offer()
    {
        const int year = 2028;
        var bank = new FakeBank();
        bank.Connect("token-suggest-skip", ("sugskip-uah", 980));
        bank.Put("sugskip-uah", new Operation("op-skip", At(year, 6, 2, 9), 450_00, 980, CounterName: "Skip Suggest Ltd"));
        await using var app = Create(At(year, 6, 10, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-suggest-skip");
        await InvoicesEndpointsTests.SaveDetails(owner);
        var buyer = await CompleteImportedClient(owner, "Skip Suggest Ltd");
        var invoice = await IssueFor(owner, buyer.Id, year, 1, 450_00, Currency.UAH);
        await Sync(app, owner);
        var imported = (await List(owner, year)).Items.Single(row => row.Source is not null);
        Assert.Equal([invoice.Id], (await Suggestions(owner))[imported.Id]);

        var confirmed = await Confirm(owner, imported);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        var after = (await List(owner, year)).Items.Single(row => row.Id == imported.Id);
        Assert.Equal((ReviewStatus.Confirmed, (Guid?)null), (after.ReviewStatus, after.InvoiceId));
        Assert.DoesNotContain(imported.Id, (await Suggestions(owner)).Keys);
        Assert.Equal((InvoiceStanding.Issued, (long?)450_00), ((await Invoice(owner, invoice.Id)).Standing, (await Invoice(owner, invoice.Id)).DueMinor));
    }

    [Fact]
    public async Task Suggestions_are_the_owners_own()
    {
        const int year = 2029;
        var bank = new FakeBank();
        bank.Connect("token-suggest-own", ("sugown-uah", 980));
        bank.Put("sugown-uah", new Operation("op-own-sug", At(year, 6, 2, 9), 470_00, 980, CounterName: "Isolated Suggest Ltd"));
        await using var app = Create(At(year, 6, 10, 10), bank);
        using var owner = await Connect(app, ApiFixture.AllowedEmail, "token-suggest-own");
        using var stranger = await ApiFixture.SignIn(app.Factory, ApiFixture.SecondAllowedEmail);
        await InvoicesEndpointsTests.SaveDetails(stranger);
        var theirs = await InvoicesEndpointsTests.CreateClient(stranger, "Isolated Suggest Ltd");
        await IssueFor(stranger, theirs.Id, year, 1, 470_00, Currency.UAH);

        await Sync(app, owner);

        Assert.Empty(await Suggestions(owner));
    }

    // The import has already made the client from the counterparty's name; the invoice needs its details.
    private static async Task<ClientResponse> CompleteImportedClient(HttpClient owner, string name)
    {
        var client = (await owner.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json))!.Single(row => row.Name == name);
        var completed = await owner.PutAsJsonAsync($"/api/clients/{client.Id}", InvoicesEndpointsTests.ClientBody(name), Json);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());

        return (await completed.Content.ReadFromJsonAsync<ClientResponse>(Json))!;
    }

    private static async Task<InvoiceResponse> IssueFor(
        HttpClient owner, Guid clientId, int year, int day, long rateMinor, Currency currency = Currency.USD) =>
        await InvoicesEndpointsTests.Issue(
            owner,
            (await InvoicesEndpointsTests.CreateDraft(
                owner,
                clientId,
                new DateOnly(year, 6, day),
                [InvoicesEndpointsTests.Line("Work", "Робота", InvoiceUnit.Service, 1_000, rateMinor)],
                currency)).Id);

    private static async Task<Dictionary<Guid, Guid[]>> Suggestions(HttpClient owner) =>
        (await owner.GetFromJsonAsync<InvoiceSuggestion[]>("/api/invoices/suggestions", Json))!
        .ToDictionary(row => row.ReceiptId, row => row.Invoices.Select(invoice => invoice.Id).ToArray());

    private static async Task<InvoiceResponse> Invoice(HttpClient owner, Guid id) =>
        (await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{id}", Json))!;
}
