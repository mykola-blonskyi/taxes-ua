using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Clients;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Tests.Features.Clients;

// The fixture shares one database across the class, so every test names its clients uniquely.
public sealed class ClientsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_client_is_created_listed_and_edited_with_every_detail()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var name = UniqueName("Acme");

        var created = await Create(owner, Details(name));

        Assert.Equal(name, created.Name);
        Assert.Equal("1 Main St, Berlin", created.Address);
        Assert.Equal("DE", created.Country);
        Assert.Equal("DE123456789", created.VatId);
        Assert.Equal("ap@acme.example", created.Email);
        Assert.Equal(Currency.EUR, created.DefaultCurrency);
        Assert.Equal("Net 14", created.Notes);
        Assert.Equal(0, created.ReceiptCount);
        Assert.Equal(created, Assert.Single(await List(owner), row => row.Id == created.Id));

        var edited = await Update(owner, created.Id, Details(name) with
        {
            Address = "2 Side St, Vienna",
            Country = "at",
            DefaultCurrency = null,
            Notes = null,
        });

        Assert.Equal("2 Side St, Vienna", edited.Address);
        Assert.Equal("AT", edited.Country);
        Assert.Null(edited.DefaultCurrency);
        Assert.Null(edited.Notes);
        Assert.Equal(edited, Assert.Single(await List(owner), row => row.Id == created.Id));
    }

    [Fact]
    public async Task Details_are_trimmed_and_blank_optional_fields_become_absent()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var name = UniqueName("Trim");

        var created = await Create(owner, new ClientRequest($"  {name}  ", "  ", " us ", "", "  ", null, "\t"));

        Assert.Equal(name, created.Name);
        Assert.Null(created.Address);
        Assert.Equal("US", created.Country);
        Assert.Null(created.VatId);
        Assert.Null(created.Email);
        Assert.Null(created.Notes);
    }

    [Fact]
    public async Task A_client_a_receipt_created_is_listed_with_a_count_and_can_be_completed()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var name = UniqueName("FromReceipt");
        await PostReceipt(owner, name, new DateOnly(2012, 1, 5));
        await PostReceipt(owner, name, new DateOnly(2012, 1, 6));

        var listed = Assert.Single(await List(owner), row => row.Name == name);
        Assert.Equal(2, listed.ReceiptCount);
        Assert.Null(listed.Country);

        var completed = await Update(owner, listed.Id, Details(name));

        Assert.Equal("DE", completed.Country);
        Assert.Equal(2, completed.ReceiptCount);
        await PostReceipt(owner, name, new DateOnly(2012, 1, 7));
        Assert.Equal(3, Assert.Single(await List(owner), row => row.Name == name).ReceiptCount);
    }

    [Fact]
    public async Task A_name_is_unique_per_owner_on_create_and_rename()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        var taken = UniqueName("Taken");
        var free = UniqueName("Free");
        await Create(owner, Details(taken));
        var second = await Create(owner, Details(free));

        var duplicate = await owner.PostAsJsonAsync("/api/clients", Details(taken), Json);
        var rename = await owner.PutAsJsonAsync($"/api/clients/{second.Id}", Details(taken), Json);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        await AssertErrorKey(duplicate, "name");
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        await AssertErrorKey(rename, "name");
        Assert.Equal(free, Assert.Single(await List(owner), row => row.Id == second.Id).Name);

        var sameNameForAnotherOwner = await Create(other, Details(taken));
        Assert.Equal(taken, sameNameForAnotherOwner.Name);
    }

    [Fact]
    public async Task Saving_a_client_under_its_own_name_is_not_a_duplicate()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var created = await Create(owner, Details(UniqueName("Same")));

        var saved = await Update(owner, created.Id, Details(created.Name) with { Notes = "Changed" });

        Assert.Equal("Changed", saved.Notes);
    }

    [Fact]
    public async Task Renaming_a_client_keeps_its_receipts_linked()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var oldName = UniqueName("Before");
        var newName = UniqueName("After");
        var receipt = await PostReceipt(owner, oldName, new DateOnly(2013, 2, 1));
        var client = Assert.Single(await List(owner), row => row.Name == oldName);

        await Update(owner, client.Id, Details(newName));

        var renamed = Assert.Single(await List(owner), row => row.Id == client.Id);
        Assert.Equal(newName, renamed.Name);
        Assert.Equal(1, renamed.ReceiptCount);
        var transactions = await owner.GetFromJsonAsync<TransactionListResponse>("/api/transactions?year=2013", Json);
        Assert.Equal(newName, Assert.Single(transactions!.Items, row => row.Id == receipt.Id).ClientName);
    }

    [Fact]
    public async Task A_client_with_receipts_cannot_be_deleted_and_one_without_can()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var name = UniqueName("Linked");
        var receipt = await PostReceipt(owner, name, new DateOnly(2014, 3, 1));
        var linked = Assert.Single(await List(owner), row => row.Name == name);
        var unused = await Create(owner, Details(UniqueName("Unused")));

        var refused = await owner.DeleteAsync($"/api/clients/{linked.Id}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 receipt", await refused.Content.ReadAsStringAsync());
        Assert.Contains(await List(owner), row => row.Id == linked.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/clients/{unused.Id}")).StatusCode);
        Assert.DoesNotContain(await List(owner), row => row.Id == unused.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/clients/{unused.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/clients/{linked.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_owner_cannot_read_edit_or_delete_another_owners_client()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        var name = UniqueName("Private");
        var created = await Create(owner, Details(name));

        Assert.DoesNotContain(await List(other), row => row.Id == created.Id);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/clients/{created.Id}", Details(UniqueName("Stolen")), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/clients/{created.Id}")).StatusCode);
        Assert.Equal(name, Assert.Single(await List(owner), row => row.Id == created.Id).Name);
    }

    [Fact]
    public async Task Anonymous_callers_are_turned_away()
    {
        using var anonymous = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/clients", Details("Nobody"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/clients/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "   ")]
    [InlineData("name", "a\u0000b")]
    [InlineData("country", "XX")]
    [InlineData("country", "UKR")]
    [InlineData("country", "1A")]
    [InlineData("email", "not an email")]
    [InlineData("email", "missing-at.example.com")]
    [InlineData("email", "a@b")]
    [InlineData("email", "Name <a@b.example>")]
    [InlineData("address", "a\u0007b")]
    [InlineData("vatId", "a\u0000b")]
    [InlineData("notes", "a\u0000b")]
    public async Task An_invalid_field_is_rejected_under_its_own_name(string field, string value)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var valid = Details(UniqueName("Invalid"));
        var body = field switch
        {
            "name" => valid with { Name = value },
            "country" => valid with { Country = value },
            "email" => valid with { Email = value },
            "address" => valid with { Address = value },
            "vatId" => valid with { VatId = value },
            _ => valid with { Notes = value },
        };

        var response = await owner.PostAsJsonAsync("/api/clients", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, field);
    }

    [Theory]
    [InlineData("name", 201)]
    [InlineData("address", 501)]
    [InlineData("vatId", 51)]
    [InlineData("notes", 2001)]
    public async Task A_field_longer_than_its_column_is_rejected(string field, int length)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var valid = Details(UniqueName("Long"));
        var value = new string('a', length);
        var body = field switch
        {
            "name" => valid with { Name = value },
            "address" => valid with { Address = value },
            "vatId" => valid with { VatId = value },
            _ => valid with { Notes = value },
        };

        var response = await owner.PostAsJsonAsync("/api/clients", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, field);
    }

    [Fact]
    public async Task An_unknown_default_currency_is_rejected()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var body = JsonSerializer.SerializeToNode(Details(UniqueName("Currency")), Json)!.AsObject();
        body["defaultCurrency"] = "GBP";

        var response = await owner.PostAsync(
            "/api/clients", new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_changes_are_written_to_the_change_log()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var name = UniqueName("Audited");
        var created = await Create(owner, Details(name));
        await Update(owner, created.Id, Details(name) with { Address = "New address" });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/clients/{created.Id}")).StatusCode);

        var response = await owner.GetAsync($"/api/audit?entity=Client&id={created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = (await response.Content.ReadFromJsonAsync<AuditEntryResponse[]>(Json))!;

        Assert.Equal([AuditAction.Delete, AuditAction.Update, AuditAction.Create], log.Select(entry => entry.Action));
        var update = log[1];
        Assert.Equal("1 Main St, Berlin", update.Before!["address"].GetString());
        Assert.Equal("New address", update.After!["address"].GetString());
        Assert.False(update.After!.ContainsKey("userId"));
    }

    private static ClientRequest Details(string name) =>
        new(name, "1 Main St, Berlin", "DE", "DE123456789", "ap@acme.example", Currency.EUR, "Net 14");

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static async Task<ClientResponse> Create(HttpClient client, ClientRequest body)
    {
        var response = await client.PostAsJsonAsync("/api/clients", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ClientResponse>(Json))!;
    }

    private static async Task<ClientResponse> Update(HttpClient client, Guid id, ClientRequest body)
    {
        var response = await client.PutAsJsonAsync($"/api/clients/{id}", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ClientResponse>(Json))!;
    }

    private static async Task<ClientResponse[]> List(HttpClient client) =>
        (await client.GetFromJsonAsync<ClientResponse[]>("/api/clients", Json))!;

    private static async Task<TransactionResponse> PostReceipt(HttpClient client, string clientName, DateOnly date)
    {
        var body = new TransactionRequest(
            date, 10_000, Currency.UAH, null, TransactionKind.Income, null, clientName, null, null, null);
        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private static async Task AssertErrorKey(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        ProblemAssert.Rejects(document.RootElement, key);
    }

    private async Task<HttpClient> SignIn(string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return client;
    }
}
