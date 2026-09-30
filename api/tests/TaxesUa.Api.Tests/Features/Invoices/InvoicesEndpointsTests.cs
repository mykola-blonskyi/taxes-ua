using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Api.Tests.Features.Settings;
using UglyToad.PdfPig;

namespace TaxesUa.Api.Tests.Features.Invoices;

// Tests share one database, so each numbers its invoices in a year of its own and names its own
// clients. The second owner never saves invoicing details, which the missing-requisites test relies on.
public sealed class InvoicesEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Today = new(2040, 3, 10);

    [Fact]
    public async Task Numbers_run_per_year_from_001_and_drafts_never_take_one()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Numbering Ltd");

        var first = await CreateDraft(owner, client.Id, new DateOnly(2041, 2, 1));
        var second = await CreateDraft(owner, client.Id, new DateOnly(2041, 1, 5));
        var abandoned = await CreateDraft(owner, client.Id, new DateOnly(2041, 1, 6));
        var nextYear = await CreateDraft(owner, client.Id, new DateOnly(2042, 1, 2));
        Assert.Null(first.Number);

        Assert.Equal("2041-001", (await Issue(owner, second.Id)).Number);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/invoices/{abandoned.Id}")).StatusCode);
        Assert.Equal("2041-002", (await Issue(owner, first.Id)).Number);
        Assert.Equal("2042-001", (await Issue(owner, nextYear.Id)).Number);

        var again = await Issue(owner, first.Id);
        Assert.Equal("2041-002", again.Number);
        Assert.Equal(InvoiceStatus.Issued, again.Status);

        var third = await CreateDraft(owner, client.Id, new DateOnly(2041, 12, 31));
        Assert.Equal("2041-003", (await Issue(owner, third.Id)).Number);

        var listed = await owner.GetFromJsonAsync<InvoiceSummary[]>("/api/invoices?year=2041", Json);
        Assert.Equal(["2041-003", "2041-002", "2041-001"], listed!.Select(row => row.Number));
    }

    [Fact]
    public async Task Concurrent_issues_get_distinct_consecutive_numbers()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Race Ltd");
        var drafts = new List<InvoiceResponse>();
        for (var i = 0; i < 8; i++)
        {
            drafts.Add(await CreateDraft(owner, client.Id, new DateOnly(2043, 3, 1)));
        }

        var issued = await Task.WhenAll(drafts.Select(draft => Issue(owner, draft.Id)));

        Assert.Equal(
            Enumerable.Range(1, 8).Select(sequence => $"2043-{sequence:000}").ToHashSet(),
            issued.Select(invoice => invoice.Number!).ToHashSet());
    }

    [Fact]
    public async Task A_draft_is_edited_and_deleted_but_an_issued_invoice_rejects_both()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Editing Ltd");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2044, 1, 10));

        var edited = await owner.PutAsJsonAsync($"/api/invoices/{draft.Id}", Request(client.Id, new DateOnly(2044, 1, 11)) with
        {
            Lines = [Line("Audit", "Аудит", InvoiceUnit.Day, 2_000, 300_00)],
        }, Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var saved = await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{draft.Id}", Json);
        Assert.Equal(new DateOnly(2044, 1, 11), saved!.IssueDate);
        Assert.Equal("Аудит", Assert.Single(saved.Lines).DescriptionUk);
        Assert.Equal(600_00, saved.TotalMinor);

        await Issue(owner, draft.Id);
        var put = await owner.PutAsJsonAsync($"/api/invoices/{draft.Id}", Request(client.Id, new DateOnly(2044, 1, 12)), Json);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/invoices/{draft.Id}")).StatusCode);
        var unchanged = await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{draft.Id}", Json);
        Assert.Equal(new DateOnly(2044, 1, 11), unchanged!.IssueDate);
        Assert.Equal(600_00, unchanged.TotalMinor);

        var other = await CreateDraft(owner, client.Id, new DateOnly(2044, 2, 1));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/invoices/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/invoices/{other.Id}")).StatusCode);
    }

    [Fact]
    public async Task Line_amounts_round_once_half_away_from_zero_and_the_total_is_their_sum()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var client = await CreateClient(owner, "Rounding Ltd");

        var draft = await CreateDraft(owner, client.Id, new DateOnly(2045, 1, 1),
        [
            Line("Hours", "Години", InvoiceUnit.Hour, 1_333, 33_33),
            Line("Half a cent", "Пів цента", InvoiceUnit.Service, 500, 1),
            Line("Seven and a half", "Сім з половиною", InvoiceUnit.Service, 2_500, 3),
            Line("Month", "Місяць", InvoiceUnit.Month, 1_000, 1_234_567_89),
        ]);

        Assert.Equal([44_43, 1, 8, 1_234_567_89], draft.Lines.Select(line => line.AmountMinor));
        Assert.Equal(44_43 + 1 + 8 + 1_234_567_89, draft.TotalMinor);
    }

    [Fact]
    public async Task A_null_line_is_a_validation_error_on_create_and_update()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var client = await CreateClient(owner, "Null Line Ltd");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2046, 5, 1));
        var body = $$"""{"clientId":"{{client.Id}}","issueDate":"2046-05-01","dueDate":"2046-05-15","currency":"USD","lines":[null]}""";

        var created = await owner.PostAsync("/api/invoices", new StringContent(body, Encoding.UTF8, "application/json"));
        var updated = await owner.PutAsync($"/api/invoices/{draft.Id}", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Contains("lines[0]", (await Errors(created)).Keys);
        Assert.Contains("lines[0]", (await Errors(updated)).Keys);
    }

    [Fact]
    public async Task A_quantity_is_at_most_100_000_units()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var client = await CreateClient(owner, "Quantity Ltd");

        var atLimit = await owner.PostAsJsonAsync("/api/invoices", Request(
            client.Id, new DateOnly(2047, 5, 1), [Line("Bulk", "Оптом", InvoiceUnit.Hour, 100_000_000, 1_00)]), Json);
        var overLimit = await owner.PostAsJsonAsync("/api/invoices", Request(
            client.Id, new DateOnly(2047, 5, 1), [Line("Bulk", "Оптом", InvoiceUnit.Hour, 100_000_001, 1_00)]), Json);

        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Contains("lines[0].quantityThousandths", (await Errors(overLimit)).Keys);
    }

    [Fact]
    public async Task A_draft_request_that_breaks_the_rules_is_rejected_with_field_errors()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        var client = await CreateClient(owner, "Rules Ltd");

        var response = await owner.PostAsJsonAsync("/api/invoices", Request(client.Id, new DateOnly(2045, 5, 1)) with
        {
            DueDate = new DateOnly(2045, 4, 30),
            Lines = [Line("A\u0000", "Б", InvoiceUnit.Hour, 0, -1)],
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.Contains("dueDate", errors.Keys);
        Assert.Contains("lines[0].descriptionEn", errors.Keys);
        Assert.Contains("lines[0].quantityThousandths", errors.Keys);

        var unknown = await owner.PostAsJsonAsync("/api/invoices", Request(Guid.NewGuid(), new DateOnly(2045, 5, 1)), Json);
        Assert.Contains("clientId", (await Errors(unknown)).Keys);
    }

    [Fact]
    public async Task Missing_requisites_block_issuing_with_errors_naming_each_one()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        var client = await CreateClient(owner, "Bare Ltd", complete: false);
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2046, 1, 1),
            [Line("Consulting", "", InvoiceUnit.Hour, 1_000, 100_00)]);

        var response = await owner.PostAsync($"/api/invoices/{draft.Id}/issue", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.Equal(
            new[]
            {
                "client.address", "client.country", "invoicing.addressEn", "invoicing.addressUk",
                "invoicing.paymentDetails.USD", "invoicing.rnokpp", "invoicing.sellerNameEn",
                "invoicing.sellerNameUk", "lines[0].descriptionUk",
            },
            errors.Keys.Order());
        Assert.Equal("Payment details for USD are missing.", errors["invoicing.paymentDetails.USD"].Single());
        var after = await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{draft.Id}", Json);
        Assert.Equal(InvoiceStatus.Draft, after!.Status);
        Assert.Null(after.Number);
    }

    [Fact]
    public async Task Issuing_in_a_currency_without_payment_details_names_that_currency()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Euro GmbH");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2046, 2, 1), currency: Currency.EUR);

        var response = await owner.PostAsync($"/api/invoices/{draft.Id}/issue", null);

        Assert.Equal(["invoicing.paymentDetails.EUR"], (await Errors(response)).Keys);
    }

    [Fact]
    public async Task The_issued_pdf_carries_every_requisite_in_both_languages()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        await UploadSignature(owner);
        var client = await CreateClient(owner, "Acme Corp, Inc.");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2047, 4, 1),
        [
            Line("Software development", "Розробка програмного забезпечення", InvoiceUnit.Hour, 10_500, 45_00),
            Line("Hosting", "Хостинг", InvoiceUnit.Month, 1_000, 99_99),
        ]);
        var issued = await Issue(owner, draft.Id);

        var response = await owner.GetAsync($"/api/invoices/{issued.Id}/pdf");

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2047-001_Acme_Corp_Inc.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var page = pdf.GetPage(1);
        Assert.Equal(595, page.Width, 0);
        Assert.Equal(842, page.Height, 0);
        var text = InvoicePdfTests.Text(pdf);
        foreach (var expected in new[]
                 {
                     "Invoice / Інвойс № 2047-001",
                     "Date of issue / Дата складання: 01.04.2047",
                     "Due date / Оплатити до: 15.04.2047",
                     "FOP Test Testovych", "ФОП Тест Тестович", "RNOKPP / РНОКПП: 1234567890",
                     "1 Testova St, Kyiv", "Київ, вул. Тестова 1",
                     "Acme Corp, Inc.", "1 Main St, Springfield", "Country / Країна: United States", "US-12-3456789",
                     "Software development", "Розробка програмного забезпечення", "hour / година", "10.5", "45.00", "472.50",
                     "Hosting", "Хостинг", "month / місяць", "99.99",
                     "Total / Разом: 572.49 USD",
                     InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX",
                     "Intermediary bank / Банк-посередник: The Bank of New York Mellon", "IRVTUS3N", "8900057610",
                     "Payment for services under invoice No. 2047-001 / Оплата послуг згідно з інвойсом № 2047-001",
                     InvoicingDefaults.AcceptanceEn, InvoicingDefaults.AcceptanceUk,
                     InvoicingDefaults.FeesEn, InvoicingDefaults.FeesUk,
                     InvoicingDefaults.TaxStatusEn, InvoicingDefaults.TaxStatusUk,
                     "FOP Test Testovych / ФОП Тест Тестович",
                 })
        {
            Assert.Contains(expected, text);
        }

        Assert.DoesNotContain("DRAFT", text);
        Assert.Single(page.GetImages());
    }

    [Fact]
    public async Task Editing_the_details_or_the_client_after_issuing_leaves_the_pdf_as_issued()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Frozen LLC");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2048, 1, 15));
        var issued = await Issue(owner, draft.Id);
        var before = await PdfText(owner, issued.Id);

        await SaveDetails(owner, sellerNameEn: "Renamed Seller", iban: InvoicingTestData.OtherValidIban);
        var renamed = await owner.PutAsJsonAsync($"/api/clients/{client.Id}", ClientBody("Frozen LLC (new)", "99 New Road, Austin"), Json);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var after = await PdfText(owner, issued.Id);
        Assert.Equal(before, after);
        Assert.Contains("Frozen LLC", after);
        Assert.DoesNotContain("Renamed Seller", after);
        Assert.DoesNotContain("99 New Road", after);
        Assert.Equal("Frozen LLC", (await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{issued.Id}", Json))!.ClientName);

        var preview = await PdfText(owner, (await CreateDraft(owner, client.Id, new DateOnly(2048, 2, 1))).Id);
        Assert.Contains("DRAFT / ЧЕРНЕТКА", preview);
        Assert.Contains("Renamed Seller", preview);
        Assert.Contains("99 New Road, Austin", preview);
        Assert.Contains(InvoicingTestData.OtherValidIban, preview);
        Assert.Contains("Invoice / Інвойс № ____", preview);
    }

    [Fact]
    public async Task A_draft_previews_with_the_draft_mark_even_while_details_are_missing()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        var client = await CreateClient(owner, "Preview Ltd", complete: false);
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2049, 1, 1));

        var response = await owner.GetAsync($"/api/invoices/{draft.Id}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("draft_Preview_Ltd.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = InvoicePdfTests.Text(pdf);
        Assert.Contains("DRAFT / ЧЕРНЕТКА", text);
        Assert.Contains("Preview Ltd", text);
    }

    [Fact]
    public async Task Cancelling_keeps_the_number_needs_a_reason_and_does_not_free_the_number()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Cancel Ltd");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2050, 1, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(owner, draft.Id, "Mistake")).StatusCode);
        var issued = await Issue(owner, draft.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await Cancel(owner, issued.Id, "   ")).StatusCode);
        var cancelled = await Cancel(owner, issued.Id, "Wrong client");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var body = await cancelled.Content.ReadFromJsonAsync<InvoiceResponse>(Json);
        Assert.Equal(InvoiceStatus.Cancelled, body!.Status);
        Assert.Equal("2050-001", body.Number);
        Assert.Equal("Wrong client", body.CancelReason);

        var twice = await (await Cancel(owner, issued.Id, "Another reason")).Content.ReadFromJsonAsync<InvoiceResponse>(Json);
        Assert.Equal("Wrong client", twice!.CancelReason);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/invoices/{issued.Id}/issue", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/invoices/{issued.Id}")).StatusCode);
        Assert.Contains("CANCELLED / АНУЛЬОВАНО", await PdfText(owner, issued.Id));

        var next = await CreateDraft(owner, client.Id, new DateOnly(2050, 2, 1));
        Assert.Equal("2050-002", (await Issue(owner, next.Id)).Number);
    }

    [Fact]
    public async Task Duplicating_any_invoice_gives_a_draft_dated_today_with_the_same_term_and_lines()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Monthly Ltd");
        var source = await CreateDraft(owner, client.Id, new DateOnly(2051, 1, 31),
            [Line("Retainer", "Абонплата", InvoiceUnit.Month, 1_000, 2_000_00)]);
        var issued = await Issue(owner, source.Id);

        var response = await owner.PostAsync($"/api/invoices/{issued.Id}/duplicate", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json);
        Assert.Equal(InvoiceStatus.Draft, copy!.Status);
        Assert.Null(copy.Number);
        Assert.NotEqual(issued.Id, copy.Id);
        Assert.Equal(Today, copy.IssueDate);
        Assert.Equal(Today.AddDays(14), copy.DueDate);
        Assert.Equal(issued.Lines, copy.Lines);
        Assert.Equal(client.Id, copy.ClientId);
    }

    [Fact]
    public async Task Another_owner_sees_and_touches_none_of_the_invoices()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Private Ltd");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2052, 1, 1));

        Assert.DoesNotContain(
            (await other.GetFromJsonAsync<InvoiceSummary[]>("/api/invoices", Json))!,
            row => row.Id == draft.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/invoices/{draft.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/invoices/{draft.Id}/issue", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/invoices/{draft.Id}/duplicate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/invoices/{draft.Id}", Request(client.Id, new DateOnly(2052, 1, 2)), Json)).StatusCode);
        var borrowed = await other.PostAsJsonAsync("/api/invoices", Request(client.Id, new DateOnly(2052, 1, 3)), Json);
        Assert.Contains("clientId", (await Errors(borrowed)).Keys);

        Assert.Null((await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{draft.Id}", Json))!.Number);
    }

    [Fact]
    public async Task Drafts_and_status_changes_are_logged_and_a_client_with_invoices_is_kept()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        var client = await CreateClient(owner, "Logged Ltd");
        var draft = await CreateDraft(owner, client.Id, new DateOnly(2053, 1, 1));
        await Issue(owner, draft.Id);
        Assert.Equal(HttpStatusCode.OK, (await Cancel(owner, draft.Id, "Duplicate")).StatusCode);

        var history = await owner.GetFromJsonAsync<AuditEntryResponse[]>(
            $"/api/audit?entity=Invoice&id={draft.Id}", Json);

        Assert.NotNull(history);
        Assert.Equal([AuditAction.Update, AuditAction.Update, AuditAction.Create], history.Select(entry => entry.Action));
        Assert.Equal("Issued", history[1].After!["status"].GetString());
        Assert.Equal("2053", history[1].After!["numberYear"].GetRawText());
        Assert.Equal("Cancelled", history[0].After!["status"].GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/clients/{client.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_issued_invoice_survives_a_backup_and_restore_with_the_same_pdf()
    {
        await using var application = App();
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SaveDetails(owner);
        await UploadSignature(owner);
        var client = await CreateClient(owner, "Backup Ltd");
        var issued = await Issue(owner, (await CreateDraft(owner, client.Id, new DateOnly(2054, 1, 1))).Id);
        var before = await PdfText(owner, issued.Id);
        var backup = await owner.GetByteArrayAsync("/api/backup");

        await SaveDetails(owner, sellerNameEn: "After Backup");
        using var content = new ByteArrayContent(backup);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/restore", content)).StatusCode);

        var restored = await owner.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{issued.Id}", Json);
        Assert.Equal(JsonSerializer.Serialize(issued, Json), JsonSerializer.Serialize(restored, Json));
        Assert.Equal(before, await PdfText(owner, issued.Id));
        Assert.Equal(backup, await owner.GetByteArrayAsync("/api/backup"));
    }

    private WebApplicationFactory<Program> App() =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(new DateTimeOffset(Today, new TimeOnly(10, 0), TimeSpan.Zero)))));

    private static async Task SaveDetails(
        HttpClient owner, string sellerNameEn = "FOP Test Testovych", string iban = InvoicingTestData.ValidIban)
    {
        var request = new InvoicingDetailsRequest(
            "ФОП Тест Тестович",
            sellerNameEn,
            "1234567890",
            "Київ, вул. Тестова 1",
            "1 Testova St, Kyiv",
            InvoicingDefaults.AcceptanceEn,
            InvoicingDefaults.AcceptanceUk,
            InvoicingDefaults.FeesEn,
            InvoicingDefaults.FeesUk,
            InvoicingDefaults.TaxStatusEn,
            InvoicingDefaults.TaxStatusUk,
            [
                new PaymentDetailsInput(
                    Currency.USD, iban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX",
                    "The Bank of New York Mellon", "IRVTUS3N", "8900057610"),
                new PaymentDetailsInput(Currency.UAH, iban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "", "", ""),
            ]);
        var response = await owner.PutAsJsonAsync("/api/settings/invoicing", request, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task UploadSignature(HttpClient owner)
    {
        using var content = new ByteArrayContent(InvoicingTestData.SignaturePng);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync("/api/settings/invoicing/signature", content)).StatusCode);
    }

    private static ClientRequest ClientBody(string name, string? address = "1 Main St, Springfield", bool complete = true) => new(
        name,
        complete ? address : null,
        complete ? "US" : null,
        complete ? "US-12-3456789" : null,
        null,
        Currency.USD,
        null);

    private static async Task<ClientResponse> CreateClient(HttpClient owner, string name, bool complete = true)
    {
        var response = await owner.PostAsJsonAsync("/api/clients", ClientBody(name, complete: complete), Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ClientResponse>(Json))!;
    }

    private static InvoiceLineRequest Line(string en, string uk, InvoiceUnit unit, long quantityThousandths, long rateMinor) =>
        new(en, uk, unit, quantityThousandths, rateMinor);

    private static InvoiceRequest Request(
        Guid clientId, DateOnly issueDate, InvoiceLineRequest[]? lines = null, Currency currency = Currency.USD) => new(
        clientId,
        issueDate,
        issueDate.AddDays(14),
        currency,
        lines ?? [Line("Consulting", "Консультації", InvoiceUnit.Hour, 1_000, 100_00)]);

    private static async Task<InvoiceResponse> CreateDraft(
        HttpClient owner,
        Guid clientId,
        DateOnly issueDate,
        InvoiceLineRequest[]? lines = null,
        Currency currency = Currency.USD)
    {
        var response = await owner.PostAsJsonAsync("/api/invoices", Request(clientId, issueDate, lines, currency), Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
    }

    private static async Task<InvoiceResponse> Issue(HttpClient owner, Guid id)
    {
        var response = await owner.PostAsync($"/api/invoices/{id}/issue", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>(Json))!;
    }

    private static Task<HttpResponseMessage> Cancel(HttpClient owner, Guid id, string reason) =>
        owner.PostAsJsonAsync($"/api/invoices/{id}/cancel", new CancelInvoiceRequest(reason), Json);

    private static async Task<string> PdfText(HttpClient owner, Guid id)
    {
        using var pdf = PdfDocument.Open(await owner.GetByteArrayAsync($"/api/invoices/{id}/pdf"));
        return InvoicePdfTests.Text(pdf);
    }

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
