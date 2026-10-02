using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
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

    private const string ShopTreasuryIban = "UA678999980313191000026007234";

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

    private static readonly DateOnly LevyEnd = new(2026, 12, 31);

    [Fact]
    public async Task A_manual_account_keeps_its_end_for_the_same_iban_and_a_new_iban_starts_without_one()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        var first = await Put(owner, new TreasuryAccountRequest(Iban, "ГУК у м.Києві", "37993783", LevyEnd));
        Assert.Equal(LevyEnd, first.ValidUntil);

        var sameIban = await Put(owner, new TreasuryAccountRequest(Iban, "ГУК у м.Києві (нова назва)", "37993783"));
        Assert.Equal(LevyEnd, sameIban.ValidUntil);

        var newIban = await Put(owner, new TreasuryAccountRequest(ShopTreasuryIban, "ГУК у м.Києві", "37993783"));
        Assert.Equal((ShopTreasuryIban, null), (newIban.Iban, newIban.ValidUntil));

        var explicitEnd = await Put(owner, new TreasuryAccountRequest(Iban, "ГУК у м.Києві", "37993783", new DateOnly(2027, 6, 30)));
        Assert.Equal(new DateOnly(2027, 6, 30), explicitEnd.ValidUntil);
    }

    [Fact]
    public async Task A_learned_account_edited_with_the_same_iban_keeps_its_end_and_reverting_drops_the_manual_one()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await SeedLearned(ApiFixture.AllowedEmail, PaymentKind.MilitaryLevy, LevyEnd);

        var edited = await Put(owner, new TreasuryAccountRequest(Iban, "ГУК у м.Києві", "37993783"));
        Assert.Equal((TreasuryAccountSource.Manual, LevyEnd), (edited.Source, edited.ValidUntil));

        var other = await Put(owner, new TreasuryAccountRequest(ShopTreasuryIban, "ГУК у м.Києві", "37993783", new DateOnly(2027, 6, 30)));
        Assert.Equal(new DateOnly(2027, 6, 30), other.ValidUntil);

        var reverted = await owner.PostAsync("/api/settings/treasury-accounts/MilitaryLevy/revert", null);
        Assert.Equal(HttpStatusCode.OK, reverted.StatusCode);
        var learned = (await reverted.Content.ReadFromJsonAsync<TreasuryAccountResponse>(Json))!;
        Assert.Equal((TreasuryAccountSource.Learned, Iban, LevyEnd), (learned.Source, learned.Iban, learned.ValidUntil));
    }

    private static async Task<TreasuryAccountResponse> Put(HttpClient owner, TreasuryAccountRequest request)
    {
        var response = await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TreasuryAccountResponse>(Json))!;
    }

    private async Task SeedLearned(string email, PaymentKind kind, DateOnly validUntil)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
        database.TreasuryAccounts.Add(new TreasuryAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            LearnedIban = Iban,
            LearnedRecipientName = "ГУК у м.Києві",
            LearnedRecipientCode = null,
            LearnedExternalId = "op-1",
            LearnedPaidOn = new DateOnly(2026, 7, 1),
            LearnedAt = DateTimeOffset.UtcNow,
            LearnedValidUntil = validUntil,
        });
        await database.SaveChangesAsync();
    }

    [Fact]
    public async Task An_end_outside_the_supported_years_is_refused()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/MilitaryLevy",
            new TreasuryAccountRequest(Iban, "ГУК у м.Києві", "37993783", new DateOnly(1999, 12, 31)),
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("validUntil", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_pasted_iban_with_no_break_spaces_or_tabs_counts_its_real_length()
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        var pasted = Iban.Insert(4, "\u00A0").Insert(9, "\t").Insert(15, "\u2009").ToLowerInvariant();

        var response = await owner.PutAsJsonAsync("/api/settings/treasury-accounts/Esv", new TreasuryAccountRequest(pasted, "ГУ ДПС", "43141912"), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Iban, (await response.Content.ReadFromJsonAsync<TreasuryAccountResponse>(Json))!.Iban);
    }

    [Theory]
    [InlineData("UA23060070000082704", "iban has 19 characters, 29 expected.")]
    [InlineData("UA2306 0070 0000 8270 4", "iban has 19 characters, 29 expected.")]
    [InlineData("", "iban has 0 characters, 29 expected.")]
    [InlineData("PL358999980333159998000026011", "iban must start with UA.")]
    [InlineData("UA35899998033315999800002601!", "iban may contain only digits and capital letters.")]
    [InlineData(ShopIban, "iban bank id must be 899998.")]
    [InlineData("UA018999980333159998000026011", "iban checksum is wrong.")]
    public async Task A_rejected_account_says_what_is_wrong_with_it(string iban, string message)
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync("/api/settings/treasury-accounts/SingleTax", new TreasuryAccountRequest(iban, "ГУК", "37993783"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(message, Assert.Single(problem.GetProperty("errors").GetProperty("iban").EnumerateArray()).GetString());
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

    [Theory]
    [InlineData("ГУК у м.Києві", null)]
    [InlineData(null, "37993783")]
    public async Task The_database_refuses_learned_recipient_details_without_a_learned_account(string? name, string? code)
    {
        await using var app = fixture.CreateApplication(_ => { });
        using var owner = await SignInEmpty(app, ApiFixture.AllowedEmail);
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id).SingleAsync();

        database.TreasuryAccounts.Add(new TreasuryAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = PaymentKind.Esv,
            LearnedRecipientName = name,
            LearnedRecipientCode = code,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
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
