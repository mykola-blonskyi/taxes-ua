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

    private static async Task<InvoiceResponse> Invoice(HttpClient owner, Guid id) =>
        (await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{id}", Json))!;
}
