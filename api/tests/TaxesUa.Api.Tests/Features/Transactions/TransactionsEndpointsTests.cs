using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Transactions;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Tests.Features.Transactions;

// ApiFixture is an IClassFixture, so this class shares one database. xUnit fixes no order between
// tests, so every test isolates by year, and only ApiFixture.AllowedEmail's Settings row is ever
// written, idempotently, with the same FopRegistrationDate every time.
public sealed class TransactionsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly FopRegistrationDate = new(2026, 3, 1);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Amount_at_or_below_zero_is_rejected(long amountMinor)
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var body = Body(amountMinor: amountMinor, valueDate: new DateOnly(2020, 1, 10));

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "amountMinor");
    }

    [Theory]
    [InlineData(nameof(TransactionKind.OwnTransfer))]
    [InlineData(nameof(TransactionKind.FxSale))]
    [InlineData(nameof(TransactionKind.OwnDeposit))]
    [InlineData(nameof(TransactionKind.ErroneousReturn))]
    [InlineData(nameof(TransactionKind.OtherNonIncome))]
    public async Task A_non_income_kind_without_a_reason_is_rejected(string kindName)
    {
        var kind = Enum.Parse<TransactionKind>(kindName);
        using var client = await SignIn(ApiFixture.AllowedEmail);

        foreach (var reason in new[] { null, "   " })
        {
            var body = Body(kind: kind, nonIncomeReason: reason, valueDate: new DateOnly(2021, 1, 10));

            var response = await client.PostAsJsonAsync("/api/transactions", body, Json);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorKey(response, "nonIncomeReason");
        }
    }

    [Fact]
    public async Task Income_with_a_reason_is_rejected()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var body = Body(
            kind: TransactionKind.Income,
            nonIncomeReason: "should not be here",
            valueDate: new DateOnly(2022, 1, 10));

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "nonIncomeReason");
    }

    [Fact]
    public async Task Create_list_update_delete_round_trip()
    {
        const int year = 2023;
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var body = Body(amountMinor: 15_000, valueDate: new DateOnly(year, 4, 5));

        var created = await client.PostAsJsonAsync("/api/transactions", body, Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdTransaction = await created.Content.ReadFromJsonAsync<TransactionResponse>(Json);
        Assert.NotNull(createdTransaction);
        Assert.Equal(15_000, createdTransaction.AmountUahKop);
        Assert.NotNull(created.Headers.Location);
        Assert.Equal($"/api/transactions/{createdTransaction.Id}", created.Headers.Location!.OriginalString);

        var listed = await client.GetFromJsonAsync<TransactionListResponse>(
            $"/api/transactions?year={year}", Json);
        Assert.NotNull(listed);
        Assert.Single(listed.Items);
        Assert.Equal(createdTransaction.Id, listed.Items[0].Id);

        var updatedBody = Body(
            amountMinor: 30_000,
            valueDate: new DateOnly(year, 5, 6),
            clientName: "  Globex  ",
            invoiceNumber: "INV-7",
            description: "  ");
        var updated = await client.PutAsJsonAsync(
            $"/api/transactions/{createdTransaction.Id}", updatedBody, Json);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedTransaction = await updated.Content.ReadFromJsonAsync<TransactionResponse>(Json);
        Assert.Equal(30_000, updatedTransaction!.AmountMinor);
        Assert.Equal(30_000, updatedTransaction.AmountUahKop);
        Assert.Equal(new DateOnly(year, 5, 6), updatedTransaction.ValueDate);
        Assert.Equal("Globex", updatedTransaction.ClientName);
        Assert.Equal("INV-7", updatedTransaction.InvoiceNumber);
        Assert.Null(updatedTransaction.Description);

        var deleted = await client.DeleteAsync($"/api/transactions/{createdTransaction.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<TransactionListResponse>(
            $"/api/transactions?year={year}", Json);
        Assert.Empty(afterDelete!.Items);
    }

    [Fact]
    public async Task Year_total_excludes_non_income_and_pre_registration_rows()
    {
        const int year = 2026;
        using var client = await SignIn(ApiFixture.AllowedEmail);
        await SetFopRegistrationDate(FopRegistrationDate);

        var income = await Create(
            client, kind: TransactionKind.Income, amountMinor: 100_000, valueDate: new DateOnly(year, 4, 1));
        var refund = await Create(
            client,
            kind: TransactionKind.RefundToClient,
            amountMinor: 20_000,
            valueDate: new DateOnly(year, 4, 2),
            nonIncomeReason: null);
        var nonIncome = await Create(
            client,
            kind: TransactionKind.OwnDeposit,
            amountMinor: 5_000,
            valueDate: new DateOnly(year, 4, 3),
            nonIncomeReason: "own funds top-up");
        var beforeRegistration = await Create(
            client,
            kind: TransactionKind.Income,
            amountMinor: 7_000,
            valueDate: new DateOnly(year, 2, 10));

        var listed = await client.GetFromJsonAsync<TransactionListResponse>(
            $"/api/transactions?year={year}", Json);

        Assert.NotNull(listed);
        Assert.Equal(80_000, listed.TotalIncomeKop);
        Assert.Equal(FopRegistrationDate, listed.FopRegistrationDate);

        var byId = listed.Items.ToDictionary(item => item.Id);
        Assert.False(byId[income.Id].BeforeRegistration);
        Assert.False(byId[refund.Id].BeforeRegistration);
        Assert.False(byId[nonIncome.Id].BeforeRegistration);
        Assert.True(byId[beforeRegistration.Id].BeforeRegistration);
    }

    [Fact]
    public async Task A_row_from_the_previous_year_is_absent_from_this_years_list()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        await Create(client, valueDate: new DateOnly(2018, 12, 31));
        await Create(client, valueDate: new DateOnly(2019, 1, 1));

        var listed = await client.GetFromJsonAsync<TransactionListResponse>(
            "/api/transactions?year=2019", Json);

        Assert.NotNull(listed);
        Assert.Single(listed.Items);
        Assert.Equal(new DateOnly(2019, 1, 1), listed.Items[0].ValueDate);
    }

    [Fact]
    public async Task An_owner_without_a_registration_date_has_no_income_and_a_null_date()
    {
        const int year = 2024;
        using var client = await SignIn(ApiFixture.SecondAllowedEmail);
        await Create(client, kind: TransactionKind.Income, amountMinor: 50_000, valueDate: new DateOnly(year, 6, 1));

        var listed = await client.GetFromJsonAsync<TransactionListResponse>(
            $"/api/transactions?year={year}", Json);

        Assert.NotNull(listed);
        Assert.Equal(0, listed.TotalIncomeKop);
        Assert.Null(listed.FopRegistrationDate);
        Assert.Single(listed.Items);
    }

    [Fact]
    public async Task Cross_owner_isolation_on_put_and_delete()
    {
        const int year = 2025;
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        var ownerRow = await Create(owner, valueDate: new DateOnly(year, 7, 1));

        var putByOther = await other.PutAsJsonAsync(
            $"/api/transactions/{ownerRow.Id}", Body(valueDate: new DateOnly(year, 7, 2)), Json);
        var deleteByOther = await other.DeleteAsync($"/api/transactions/{ownerRow.Id}");

        Assert.Equal(HttpStatusCode.NotFound, putByOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteByOther.StatusCode);

        var ownerList = await owner.GetFromJsonAsync<TransactionListResponse>(
            $"/api/transactions?year={year}", Json);
        Assert.NotNull(ownerList);
        Assert.Single(ownerList.Items);
        Assert.Equal(ownerRow.Id, ownerList.Items[0].Id);
    }

    [Fact]
    public async Task The_same_client_name_on_two_receipts_creates_one_client()
    {
        const int year = 2028;
        using var client = await SignIn(ApiFixture.AllowedEmail);
        const string clientName = "Acme LLC";

        await Create(client, valueDate: new DateOnly(year, 1, 5), clientName: clientName);
        await Create(client, valueDate: new DateOnly(year, 1, 6), clientName: clientName);

        var clients = await client.GetFromJsonAsync<string[]>("/api/clients", Json);

        Assert.NotNull(clients);
        Assert.Single(clients, name => name == clientName);
    }

    [Fact]
    public async Task A_client_name_longer_than_its_column_is_rejected()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var body = Body(valueDate: new DateOnly(2029, 1, 1), clientName: new string('a', 201));

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "clientName");
    }

    [Fact]
    public async Task Year_out_of_range_is_rejected()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);

        var response = await client.GetAsync("/api/transactions?year=1999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorKey(response, "year");
    }

    private static async Task AssertErrorKey(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(key, out _));
    }

    private async Task<TransactionResponse> Create(
        HttpClient client,
        TransactionKind kind = TransactionKind.Income,
        long amountMinor = 10_000,
        DateOnly? valueDate = null,
        string? nonIncomeReason = null,
        string? clientName = null)
    {
        var body = Body(
            kind: kind,
            amountMinor: amountMinor,
            valueDate: valueDate ?? new DateOnly(2030, 1, 1),
            nonIncomeReason: nonIncomeReason,
            clientName: clientName);

        var response = await client.PostAsJsonAsync("/api/transactions", body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(Json))!;
    }

    private async Task SetFopRegistrationDate(DateOnly date)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users
            .Where(user => user.Email == ApiFixture.AllowedEmail)
            .Select(user => user.Id)
            .SingleAsync();

        var stored = await database.Settings.FindAsync([userId]);
        if (stored is null)
        {
            stored = new SettingsEntity { UserId = userId };
            database.Settings.Add(stored);
        }

        stored.FopRegistrationDate = date;
        await database.SaveChangesAsync();
    }

    private static Dictionary<string, object?> Body(
        TransactionKind kind = TransactionKind.Income,
        long amountMinor = 10_000,
        DateOnly valueDate = default,
        string? nonIncomeReason = null,
        string? clientName = null,
        string? invoiceNumber = null,
        string? description = null) => new()
    {
        ["valueDate"] = (valueDate == default ? new DateOnly(2030, 1, 1) : valueDate).ToString("yyyy-MM-dd"),
        ["amountMinor"] = amountMinor,
        ["kind"] = kind.ToString(),
        ["nonIncomeReason"] = nonIncomeReason,
        ["clientName"] = clientName,
        ["invoiceNumber"] = invoiceNumber,
        ["description"] = description,
    };

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
