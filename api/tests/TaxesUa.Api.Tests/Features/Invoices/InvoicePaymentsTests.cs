using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Dashboard;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using static TaxesUa.Api.Tests.Features.Invoices.InvoicesEndpointsTests;

namespace TaxesUa.Api.Tests.Features.Invoices;

// Receipts paying invoices (#93). Tests share one database, so each names its own clients and issues
// in a year of its own; the dashboard's overdue count is read as a change, not a total.
public sealed class InvoicePaymentsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateOnly Today = new(2040, 3, 10);

    [Fact]
    public async Task Partial_payments_leave_an_amount_due_and_covering_the_total_makes_the_invoice_paid()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Partial Pay Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2050, 3, 1), 1_000_00);
        var first = await Receipt(owner, "Partial Pay Ltd", 400_00);
        var second = await Receipt(owner, "Partial Pay Ltd", 600_00, new DateOnly(2040, 3, 9));
        var spare = await Receipt(owner, "Partial Pay Ltd", 50_00);

        var partly = await Link(owner, invoice.Id, first.Id);
        Assert.Equal((InvoiceStanding.Issued, 400_00L, (long?)600_00), (partly.Standing, partly.PaidMinor, partly.DueMinor));
        var listed = Assert.Single(await List(owner, 2050), row => row.Id == invoice.Id);
        Assert.Equal((InvoiceStanding.Issued, (long?)600_00), (listed.Standing, listed.DueMinor));

        var paid = await Link(owner, invoice.Id, second.Id);
        Assert.Equal((InvoiceStanding.Paid, 1_000_00L, (long?)0), (paid.Standing, paid.PaidMinor, paid.DueMinor));
        Assert.Equal([first.Id, second.Id], paid.Receipts.OrderBy(receipt => receipt.AmountMinor).Select(receipt => receipt.Id));
        Assert.Equal(InvoiceStanding.Paid, Assert.Single(await List(owner, 2050), row => row.Id == invoice.Id).Standing);

        var linkedReceipt = await Transaction(owner, second.Id);
        Assert.Equal((invoice.Id, "2050-001"), (linkedReceipt.InvoiceId!.Value, linkedReceipt.InvoiceNumber));

        Assert.Equal(1_000_00, (await Link(owner, invoice.Id, second.Id)).PaidMinor);
        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, invoice.Id, spare.Id)).StatusCode);
        Assert.Null((await Transaction(owner, spare.Id)).InvoiceId);
    }

    [Fact]
    public async Task A_receipt_in_another_currency_or_already_linked_and_a_draft_cancelled_or_foreign_invoice_are_rejected()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Rejecting Ltd");
        var issued = await IssuedInvoice(owner, client.Id, new DateOnly(2051, 3, 1), 1_000_00);
        var elsewhere = await IssuedInvoice(owner, client.Id, new DateOnly(2051, 3, 2), 1_000_00);
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2051, 3, 3));
        var cancelled = await IssuedInvoice(owner, client.Id, new DateOnly(2051, 3, 4), 1_000_00);
        Assert.Equal(HttpStatusCode.OK, (await Cancel(owner, cancelled.Id, "Wrong client")).StatusCode);
        var euros = await Receipt(owner, "Rejecting Ltd", 1_000_00, currency: Currency.EUR);
        var receipt = await Receipt(owner, "Rejecting Ltd", 1_000_00);
        var linkedElsewhere = await Receipt(owner, "Rejecting Ltd", 500_00);
        await Link(owner, elsewhere.Id, linkedElsewhere.Id);
        var refund = await Record(owner, new TransactionRequest(
            Today, 100_00, Currency.USD, 41_0000, TransactionKind.RefundToClient, null, "Rejecting Ltd", null, null, null));
        var strangersReceipt = await Receipt(other, "Rejecting Ltd", 1_000_00);

        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, issued.Id, euros.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, draft.Id, receipt.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, cancelled.Id, receipt.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, issued.Id, linkedElsewhere.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TryLink(owner, issued.Id, refund.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TryLink(other, issued.Id, strangersReceipt.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TryLink(owner, issued.Id, strangersReceipt.Id)).StatusCode);

        var unchanged = await Get(owner, issued.Id);
        Assert.Equal((InvoiceStanding.Issued, 0L), (unchanged.Standing, unchanged.PaidMinor));
        Assert.Empty(unchanged.Receipts);
        Assert.Equal(elsewhere.Id, (await Transaction(owner, linkedElsewhere.Id)).InvoiceId);
        Assert.Null((await Transaction(owner, euros.Id)).InvoiceId);
        Assert.Null((await Transaction(other, strangersReceipt.Id)).InvoiceId);
    }

    [Fact]
    public async Task A_receipt_of_another_client_may_pay_through_an_intermediary_and_keeps_its_client()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Same Client Ltd");
        await CreateClient(owner, "Intermediary Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2058, 3, 1), 1_000_00);
        var intermediary = await Receipt(owner, "Intermediary Ltd", 600_00);
        var nameless = await Receipt(owner, null, 400_00);

        var viaIntermediary = await Link(owner, invoice.Id, intermediary.Id);

        Assert.Equal(600_00, viaIntermediary.PaidMinor);
        var kept = await Transaction(owner, intermediary.Id);
        Assert.Equal((invoice.Id, "Intermediary Ltd"), (kept.InvoiceId!.Value, kept.ClientName));

        var paid = await Link(owner, invoice.Id, nameless.Id);

        Assert.Equal(InvoiceStanding.Paid, paid.Standing);
        var linked = await Transaction(owner, nameless.Id);
        Assert.Equal((invoice.Id, "Same Client Ltd"), (linked.InvoiceId!.Value, linked.ClientName));
    }

    [Fact]
    public async Task The_pickers_offer_only_what_can_be_linked()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Picker Ltd");
        await CreateClient(owner, "Picker Other Ltd");
        var later = await IssuedInvoice(owner, client.Id, new DateOnly(2052, 3, 1), 500_00, dueDate: new DateOnly(2052, 4, 1));
        var sooner = await IssuedInvoice(owner, client.Id, new DateOnly(2052, 3, 2), 500_00, dueDate: new DateOnly(2052, 3, 20));
        var paid = await IssuedInvoice(owner, client.Id, new DateOnly(2052, 3, 3), 100_00);
        var older = await Receipt(owner, "Picker Ltd", 100_00, new DateOnly(2040, 1, 5));
        var newer = await Receipt(owner, "Picker Ltd", 200_00, new DateOnly(2040, 2, 5));
        var nameless = await Receipt(owner, null, 300_00, new DateOnly(2040, 1, 1));
        var otherClients = await Receipt(owner, "Picker Other Ltd", 400_00);
        var euros = await Receipt(owner, "Picker Ltd", 400_00, currency: Currency.EUR);
        var paying = await Receipt(owner, "Picker Ltd", 100_00);
        await Link(owner, paid.Id, paying.Id);

        var receipts = await owner.GetFromJsonAsync<ReceiptOption[]>($"/api/invoices/{later.Id}/receipt-options", Json);
        // Whatever the client: a client may pay through an intermediary.
        // Tests share the owner, so other tests' unlinked USD receipts are listed too: look at this test's own.
        Guid[] mine = [otherClients.Id, newer.Id, older.Id, nameless.Id, euros.Id, paying.Id];
        Assert.Equal([otherClients.Id, newer.Id, older.Id, nameless.Id], receipts!.Select(option => option.Id).Where(mine.Contains));

        var invoices = await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices/payable-by/{newer.Id}", Json);
        Assert.Equal([sooner.Id, later.Id], invoices!.Select(invoice => invoice.Id));
        var forNameless = await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices/payable-by/{nameless.Id}", Json);
        Assert.Contains(sooner.Id, forNameless!.Select(invoice => invoice.Id));
        Assert.DoesNotContain(
            (await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices/payable-by/{otherClients.Id}", Json))!,
            invoice => invoice.ClientId == client.Id);
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices/payable-by/{euros.Id}", Json))!);
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices/payable-by/{paying.Id}", Json))!);
    }

    [Fact]
    public async Task Unlinking_and_a_full_refund_reopen_the_invoice()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Reopening Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2053, 3, 1), 1_000_00);
        var receipt = await Receipt(owner, "Reopening Ltd", 1_000_00);
        var manual = await Record(owner, ReceiptRequest("Reopening Ltd", 10_00) with { InvoiceNumber = "INV-7" });

        Assert.Equal(InvoiceStanding.Paid, (await Link(owner, invoice.Id, receipt.Id)).Standing);
        var unlinked = await owner.DeleteAsync($"/api/invoices/{invoice.Id}/receipts/{receipt.Id}");
        Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        var reopened = (await unlinked.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
        Assert.Equal((InvoiceStanding.Issued, (long?)1_000_00), (reopened.Standing, reopened.DueMinor));
        var cleared = await Transaction(owner, receipt.Id);
        Assert.Equal((null, null), (cleared.InvoiceId, cleared.InvoiceNumber));
        Assert.Equal("INV-7", (await Transaction(owner, manual.Id)).InvoiceNumber);

        Assert.Equal(InvoiceStanding.Paid, (await Link(owner, invoice.Id, receipt.Id)).Standing);
        await Record(owner, RefundOf(receipt.Id, 300_00));
        Assert.Equal((InvoiceStanding.Issued, (long?)300_00), await StandingOf(owner, invoice.Id));
        var rest = await Record(owner, RefundOf(receipt.Id, 700_00));
        Assert.Equal((InvoiceStanding.Issued, (long?)1_000_00), await StandingOf(owner, invoice.Id));
        Assert.Equal(1_000_00, Assert.Single((await Get(owner, invoice.Id)).Receipts).RefundedMinor);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{rest.Id}")).StatusCode);
        Assert.Equal((InvoiceStanding.Issued, (long?)300_00), await StandingOf(owner, invoice.Id));
    }

    [Fact]
    public async Task Deleting_a_linked_receipt_unlinks_it_and_a_linked_receipt_keeps_its_kind_currency_and_number()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Deleting Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2054, 3, 1), 1_000_00);
        var receipt = await Receipt(owner, "Deleting Ltd", 1_000_00);
        await Link(owner, invoice.Id, receipt.Id);
        var linked = ReceiptRequest("Deleting Ltd", 1_000_00) with { InvoiceNumber = "2054-001" };

        var edits = new[]
        {
            linked with { Kind = TransactionKind.OtherNonIncome, NonIncomeReason = "Not a receipt" },
            linked with { Currency = Currency.EUR },
            linked with { InvoiceNumber = "2054-999" },
        };
        foreach (var edit in edits)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/transactions/{receipt.Id}", edit, Json)).StatusCode);
        }

        var smaller = await owner.PutAsJsonAsync($"/api/transactions/{receipt.Id}", linked with { AmountMinor = 900_00 }, Json);
        Assert.Equal(HttpStatusCode.OK, smaller.StatusCode);
        Assert.Equal((InvoiceStanding.Issued, (long?)100_00), await StandingOf(owner, invoice.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(owner, invoice.Id, "Paid elsewhere")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{receipt.Id}")).StatusCode);
        var reopened = await Get(owner, invoice.Id);
        Assert.Equal((InvoiceStanding.Issued, (long?)1_000_00), (reopened.Standing, reopened.DueMinor));
        Assert.Empty(reopened.Receipts);
        Assert.Equal(HttpStatusCode.OK, (await Cancel(owner, invoice.Id, "Paid elsewhere")).StatusCode);
    }

    [Fact]
    public async Task Overdue_starts_on_the_day_after_the_due_date_in_Kyiv_and_the_home_screen_counts_it()
    {
        var dueDate = new DateOnly(2040, 3, 10);
        DateTimeOffset lastMinute = new(2040, 3, 10, 21, 59, 0, TimeSpan.Zero);
        var nextDay = lastMinute.AddMinutes(1);
        Guid invoiceId;
        int countBefore;
        await using (var application = App(lastMinute))
        {
            using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
            await SaveDetails(owner);
            var client = await CreateClient(owner, "Overdue Ltd");
            invoiceId = (await IssuedInvoice(owner, client.Id, new DateOnly(2040, 3, 1), 500_00, dueDate: dueDate)).Id;

            Assert.Equal(InvoiceStanding.Issued, (await Get(owner, invoiceId)).Standing);
            countBefore = (await Dashboard(owner)).OverdueInvoiceCount;
        }

        await using (var application = App(nextDay))
        {
            using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
            Assert.Equal((InvoiceStanding.Overdue, (long?)500_00), await StandingOf(owner, invoiceId));
            Assert.Equal(countBefore + 1, (await Dashboard(owner)).OverdueInvoiceCount);

            var receipt = await Receipt(owner, "Overdue Ltd", 500_00, new DateOnly(2040, 3, 10));
            Assert.Equal(InvoiceStanding.Paid, (await Link(owner, invoiceId, receipt.Id)).Standing);
            Assert.Equal(countBefore, (await Dashboard(owner)).OverdueInvoiceCount);
        }
    }

    [Fact]
    public async Task Exports_carry_the_linked_invoice_number()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Export Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2055, 3, 1), 1_000_00);
        var receipt = await Receipt(owner, "Export Ltd", 1_000_00, new DateOnly(2039, 6, 1));
        await Link(owner, invoice.Id, receipt.Id);

        var csv = await owner.GetStringAsync("/api/export/transactions.csv?year=2039");

        Assert.Contains(csv.Split('\n'), line => line.Contains("Export Ltd", StringComparison.Ordinal) && line.Contains("2055-001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_backup_round_trip_keeps_the_links_and_a_link_that_breaks_the_rules_is_refused()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Backup Pay Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2056, 3, 1), 1_000_00);
        var receipt = await Receipt(owner, "Backup Pay Ltd", 400_00);
        await Link(owner, invoice.Id, receipt.Id);
        var before = await Get(owner, invoice.Id);
        var backup = await owner.GetByteArrayAsync("/api/backup");

        Assert.Equal(HttpStatusCode.OK, (await Restore(owner, Encoding.UTF8.GetString(backup))).StatusCode);

        var after = await Get(owner, invoice.Id);
        Assert.Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json));
        Assert.Equal(backup, await owner.GetByteArrayAsync("/api/backup"));

        var root = JsonNode.Parse(backup)!.AsObject();
        var row = root["transactions"]!.AsArray().Single(each => each!["id"]!.GetValue<Guid>() == receipt.Id)!;
        row["currency"] = "EUR";
        var refused = await Restore(owner, root.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("invoiceId", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(InvoiceStanding.Issued, (await Get(owner, invoice.Id)).Standing);
        Assert.Equal(400_00, (await Get(owner, invoice.Id)).PaidMinor);

        var version7 = JsonNode.Parse(backup)!.AsObject();
        version7["schemaVersion"] = 7;
        foreach (var each in version7["transactions"]!.AsArray())
        {
            each!.AsObject().Remove("invoiceId");
        }

        Assert.Equal(HttpStatusCode.OK, (await Restore(owner, version7.ToJsonString())).StatusCode);
        Assert.Empty((await Get(owner, invoice.Id)).Receipts);
        Assert.Equal(HttpStatusCode.OK, (await Restore(owner, Encoding.UTF8.GetString(backup))).StatusCode);
    }

    [Fact]
    public async Task Another_owner_can_neither_see_nor_change_the_links()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Isolated Pay Ltd");
        var invoice = await IssuedInvoice(owner, client.Id, new DateOnly(2057, 3, 1), 1_000_00);
        var receipt = await Receipt(owner, "Isolated Pay Ltd", 1_000_00);
        await Link(owner, invoice.Id, receipt.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/invoices/{invoice.Id}/receipts/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/invoices/{invoice.Id}/receipt-options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/invoices/payable-by/{receipt.Id}")).StatusCode);
        Assert.Equal(InvoiceStanding.Paid, (await Get(owner, invoice.Id)).Standing);
    }

    private WebApplicationFactory<Program> App(DateTimeOffset? now = null) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(now ?? new DateTimeOffset(Today, new TimeOnly(10, 0), TimeSpan.Zero)))));

    private static async Task<InvoiceResponse> IssuedInvoice(
        HttpClient owner, Guid clientId, DateOnly issueDate, long totalMinor, DateOnly? dueDate = null)
    {
        var request = Request(clientId, issueDate, [Line("Consulting", "Консультації", InvoiceUnit.Service, 1_000, totalMinor)]);
        var response = await owner.PostAsJsonAsync("/api/invoices", request with { DueDate = dueDate ?? request.DueDate }, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var draft = (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;

        return await Issue(owner, draft.Id);
    }

    private static TransactionRequest ReceiptRequest(
        string? clientName, long amountMinor, DateOnly? valueDate = null, Currency currency = Currency.USD) => new(
        valueDate ?? Today, amountMinor, currency, 41_0000, TransactionKind.Income, null, clientName, null, null, null);

    private static TransactionRequest RefundOf(Guid receiptId, long amountMinor) => new(
        Today, amountMinor, Currency.USD, 41_0000, TransactionKind.RefundToClient, null, null, null, null, receiptId);

    private static Task<TransactionResponse> Receipt(
        HttpClient owner, string? clientName, long amountMinor, DateOnly? valueDate = null, Currency currency = Currency.USD) =>
        Record(owner, ReceiptRequest(clientName, amountMinor, valueDate, currency));

    private static async Task<TransactionResponse> Record(HttpClient owner, TransactionRequest request)
    {
        var response = await owner.PostAsJsonAsync("/api/transactions", request, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private static async Task<TransactionResponse> Transaction(HttpClient owner, Guid id)
    {
        foreach (var year in new[] { 2039, 2040 })
        {
            var list = await owner.GetFromJsonAsync<TransactionListResponse>($"/api/transactions?year={year}", Json);
            if (list!.Items.FirstOrDefault(row => row.Id == id) is { } row)
            {
                return row;
            }
        }

        throw new InvalidOperationException($"Transaction {id} is not listed.");
    }

    private static Task<HttpResponseMessage> TryLink(HttpClient owner, Guid invoiceId, Guid receiptId) =>
        owner.PostAsync($"/api/invoices/{invoiceId}/receipts/{receiptId}", null);

    private static async Task<InvoiceResponse> Link(HttpClient owner, Guid invoiceId, Guid receiptId)
    {
        var response = await TryLink(owner, invoiceId, receiptId);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
    }

    private static async Task<InvoiceResponse> Get(HttpClient owner, Guid id) =>
        (await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{id}", Json))!;

    private static async Task<(InvoiceStanding, long?)> StandingOf(HttpClient owner, Guid id)
    {
        var invoice = await Get(owner, id);
        return (invoice.Standing, invoice.DueMinor);
    }

    private static async Task<InvoiceSummary[]> List(HttpClient owner, int year) =>
        (await owner.GetFromJsonAsync<InvoiceSummary[]>($"/api/invoices?year={year}", Json))!;

    private static async Task<DashboardResponse> Dashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static Task<HttpResponseMessage> Restore(HttpClient owner, string json) =>
        owner.PostAsync("/api/restore", new StringContent(json, Encoding.UTF8, "application/json"));
}
