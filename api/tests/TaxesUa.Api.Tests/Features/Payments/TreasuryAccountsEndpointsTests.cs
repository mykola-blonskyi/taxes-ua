using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Payments;

// Manual entry of Treasury accounts (#98). A restore of an empty file gives each test a clean owner; the
// learning from confirmed candidates is tested with the bank harness in MonobankSyncTests.TreasuryAccounts.
public sealed class TreasuryAccountsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Empty =
        """{"schemaVersion":9,"settings":null,"clients":[],"transactions":[],"budgetPayments":[],"bankAccounts":[],"importBatches":[],"budgetPaymentCandidates":[],"invoicingDetails":null,"invoices":[],"declarationDetails":null,"declarationFilings":[],"treasuryAccounts":[]}""";

    private const string Iban = "UA358999980333159998000026011";

    private const string ShopIban = "UA753220010000026001234567891";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_new_owner_has_no_account_for_any_kind()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var accounts = await Accounts(owner);

        Assert.Equal(
            [PaymentKind.SingleTax, PaymentKind.MilitaryLevy, PaymentKind.Esv],
            accounts.Select(account => account.Kind));
        Assert.All(accounts, account =>
        {
            Assert.Equal(TreasuryAccountSource.None, account.Source);
            Assert.Null(account.Iban);
            Assert.False(account.HasLearned);
            Assert.Empty(account.Missing);
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync("/api/settings/treasury-accounts")).StatusCode);
    }

    [Fact]
    public async Task A_manual_account_is_stored_normalized_and_shown_as_entered()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/Esv",
            new TreasuryAccountRequest("ua35 8999 9803 3315 9998 0000 2601 1", "  ГУ ДПС у м.Києві  ", " 43141912 "),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<TreasuryAccountResponse>(Json))!;
        Assert.Equal(
            (PaymentKind.Esv, TreasuryAccountSource.Manual, Iban, "ГУ ДПС у м.Києві", "43141912", false),
            (account.Kind, account.Source, account.Iban, account.RecipientName, account.RecipientCode, account.HasLearned));
        Assert.NotNull(account.UpdatedAt);
        Assert.Equal(account.Iban, (await Accounts(owner)).Single(row => row.Kind == PaymentKind.Esv).Iban);
        Assert.Equal(TreasuryAccountSource.None, (await Accounts(owner)).Single(row => row.Kind == PaymentKind.SingleTax).Source);

        var history = await owner.GetFromJsonAsync<JsonElement>("/api/audit?entity=TreasuryAccount", Json);
        var entry = history.EnumerateArray().First(row =>
            row.GetProperty("after").GetProperty("manualRecipientName").GetString() == "ГУ ДПС у м.Києві");
        Assert.Equal(("Esv", Iban), (
            entry.GetProperty("after").GetProperty("kind").GetString(),
            entry.GetProperty("after").GetProperty("manualIban").GetString()));
    }

    [Theory]
    [InlineData(ShopIban, "ГУК", "37993783", "iban")]
    [InlineData("UA018999980333159998000026011", "ГУК", "37993783", "iban")]
    [InlineData("UA35899998033315999800002601", "ГУК", "37993783", "iban")]
    [InlineData("", "ГУК", "37993783", "iban")]
    [InlineData(Iban, "", "37993783", "recipientName")]
    [InlineData(Iban, "   ", "37993783", "recipientName")]
    [InlineData(Iban, "ГУК\u0007", "37993783", "recipientName")]
    [InlineData(Iban, "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345", "37993783", "recipientName")]
    [InlineData(Iban, "ГУК", "3799378", "recipientCode")]
    [InlineData(Iban, "ГУК", "379937830", "recipientCode")]
    [InlineData(Iban, "ГУК", "3799378a", "recipientCode")]
    [InlineData(Iban, "ГУК", "", "recipientCode")]
    public async Task An_account_that_is_not_a_Treasury_account_or_lacks_a_name_or_an_8_digit_code_is_rejected(
        string iban, string name, string code, string rejectedField)
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/settings/treasury-accounts/SingleTax", new TreasuryAccountRequest(iban, name, code), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(rejectedField, problem.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(TreasuryAccountSource.None, (await Accounts(owner)).Single(row => row.Kind == PaymentKind.SingleTax).Source);
    }

    [Fact]
    public async Task Reverting_without_a_learned_account_and_an_unknown_kind_are_refused()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/SingleTax", new TreasuryAccountRequest(Iban, "ГУК", "37993783"), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/settings/treasury-accounts/SingleTax/revert", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/settings/treasury-accounts/Esv/revert", null)).StatusCode);
        Assert.Equal(TreasuryAccountSource.Manual, (await Accounts(owner)).Single(row => row.Kind == PaymentKind.SingleTax).Source);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/7", new TreasuryAccountRequest(Iban, "ГУК", "37993783"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync("/api/settings/treasury-accounts/7/revert", null)).StatusCode);
    }

    [Fact]
    public async Task One_owner_never_sees_or_changes_another_owners_accounts()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        using var stranger = await SignInEmpty(app, ApiFixture.SecondAllowedEmail);

        await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", new TreasuryAccountRequest(Iban, "ГУК", "37993783"), Json);

        Assert.All(await Accounts(stranger), account => Assert.Equal(TreasuryAccountSource.None, account.Source));
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.PostAsync("/api/settings/treasury-accounts/MilitaryLevy/revert", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await stranger.PostAsync("/api/settings/treasury-accounts/MilitaryLevy/notice/dismiss", null)).StatusCode);
        Assert.Equal(Iban, (await Accounts(owner)).Single(row => row.Kind == PaymentKind.MilitaryLevy).Iban);
    }

    private async Task<HttpClient> SignInEmpty(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string email)
    {
        var client = await ApiFixture.SignIn(app, email);
        var response = await client.PostAsync("/api/restore", new StringContent(Empty, Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<TreasuryAccountResponse[]> Accounts(HttpClient owner) =>
        (await owner.GetFromJsonAsync<TreasuryAccountResponse[]>("/api/settings/treasury-accounts", Json))!;
}
