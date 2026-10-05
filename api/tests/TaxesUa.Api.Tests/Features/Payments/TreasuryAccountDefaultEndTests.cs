using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Payments;

// The 2026 temporary military-levy accounts end on the seeded 2026-12-31 unless the owner says otherwise (#261,
// Rule 16). The Q4 2026 levy is due 2027-02-19, so in December the account warns and from January it is expired.
// This class owns its database, and no test in it writes the 2026 tax year.
public sealed class TreasuryAccountDefaultEndTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Empty =
        """{"schemaVersion":9,"settings":null,"clients":[],"transactions":[],"budgetPayments":[],"bankAccounts":[],"importBatches":[],"budgetPaymentCandidates":[],"invoicingDetails":null,"invoices":[],"declarationDetails":null,"declarationFilings":[],"treasuryAccounts":[]}""";

    private const string LearnedIban = "UA018999980333159998000026011";

    private const string NewIban = "UA358999980333159998000026011";

    private const string Q4Levy = "kind=MilitaryLevy&periodYear=2026&periodQuarter=4&amountKop=123456";

    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private static readonly DateOnly FirstMonday = new(2027, 1, 4);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task On_the_last_day_a_learned_levy_account_ends_then_by_default_and_the_q4_levy_still_pays_into_it()
    {
        await using var app = AppOn(YearEnd);
        using var owner = await SignInEmpty(app);
        await SeedLearned(PaymentKind.MilitaryLevy);
        await SeedLearned(PaymentKind.Esv);

        var accounts = await Accounts(owner);
        var levy = accounts.Single(account => account.Kind == PaymentKind.MilitaryLevy);
        var esv = accounts.Single(account => account.Kind == PaymentKind.Esv);
        Assert.Equal((YearEnd, TreasuryEndSource.Default), (levy.ValidUntil, levy.ValidUntilSource));
        Assert.Equal((null, null), (esv.ValidUntil, esv.ValidUntilSource));

        var q4 = await Details(owner, Q4Levy);
        Assert.Equal((YearEnd, PaymentAccountExpiryState.ExpiresBeforeDue), (q4.Expiry!.ValidUntil, q4.Expiry.State));
        Assert.Equal(LearnedIban, q4.Recipient!.Iban);
        Assert.NotNull(q4.QrContent);
        Assert.Empty(await ExpiredOnDashboard(owner));
    }

    [Fact]
    public async Task From_january_the_default_end_has_passed_so_the_pay_panel_withholds_the_account_and_the_dashboard_asks_for_the_new_one()
    {
        await using var app = AppOn(FirstMonday);
        using var owner = await SignInEmpty(app);
        await SeedLearned(PaymentKind.MilitaryLevy);

        var q4 = await Details(owner, Q4Levy);

        Assert.Equal((YearEnd, PaymentAccountExpiryState.Expired), (q4.Expiry!.ValidUntil, q4.Expiry.State));
        Assert.Null(q4.Recipient);
        Assert.Null(q4.QrContent);
        var expired = Assert.Single(await ExpiredOnDashboard(owner));
        Assert.Equal(("MilitaryLevy", "2026-12-31"), (expired!["kind"]!.GetValue<string>(), expired["validUntil"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_end_the_owner_set_wins_over_the_default_and_is_reported_as_the_owners()
    {
        await using var app = AppOn(FirstMonday);
        using var owner = await SignInEmpty(app);
        await SeedLearned(PaymentKind.MilitaryLevy);

        await SetValidUntil(owner, new DateOnly(2027, 3, 31));

        var levy = await Levy(owner);
        Assert.Equal((new DateOnly(2027, 3, 31), TreasuryEndSource.Owner), (levy.ValidUntil, levy.ValidUntilSource));
        Assert.Null((await Details(owner, Q4Levy)).Expiry);
        Assert.Empty(await ExpiredOnDashboard(owner));
    }

    [Fact]
    public async Task A_removed_end_sets_the_default_aside_and_survives_a_backup_and_restore()
    {
        await using var app = AppOn(FirstMonday);
        using var owner = await SignInEmpty(app);
        await SeedLearned(PaymentKind.MilitaryLevy);

        await SetValidUntil(owner, null);

        Assert.Equal((null, null), ((await Levy(owner)).ValidUntil, (await Levy(owner)).ValidUntilSource));
        Assert.Null((await Details(owner, Q4Levy)).Expiry);
        Assert.Empty(await ExpiredOnDashboard(owner));

        var backup = await owner.GetStringAsync("/api/backup");
        var account = JsonNode.Parse(backup)!["treasuryAccounts"]!.AsArray().Single()!;
        Assert.True(account["learnedEndRemoved"]!.GetValue<bool>());
        await Restore(owner, Empty);
        Assert.Equal(TreasuryAccountSource.None, (await Levy(owner)).Source);
        await Restore(owner, backup);

        var restored = await Levy(owner);
        Assert.Equal((LearnedIban, null), (restored.Iban, restored.ValidUntil));
        Assert.Null((await Details(owner, Q4Levy)).Expiry);
    }

    [Fact]
    public async Task A_new_account_entered_after_the_end_has_no_default_and_clears_the_prompt()
    {
        await using var app = AppOn(FirstMonday);
        using var owner = await SignInEmpty(app);
        await SeedLearned(PaymentKind.MilitaryLevy);
        Assert.Single(await ExpiredOnDashboard(owner));

        var entered = await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/MilitaryLevy", new TreasuryAccountRequest(NewIban, "ГУК у м.Києві", "37993783"), Json);
        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);

        Assert.Equal((NewIban, null), ((await Levy(owner)).Iban, (await Levy(owner)).ValidUntil));
        Assert.Empty(await ExpiredOnDashboard(owner));
        Assert.Equal(NewIban, (await Details(owner, Q4Levy)).Recipient!.Iban);
    }

    [Fact]
    public async Task A_levy_account_entered_during_the_year_gets_the_default_and_keeps_it_when_entered_again()
    {
        await using var app = AppOn(new DateOnly(2026, 11, 2));
        using var owner = await SignInEmpty(app);
        var request = new TreasuryAccountRequest(NewIban, "ГУК у м.Києві", "37993783");

        await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", request, Json);
        Assert.Equal((YearEnd, TreasuryEndSource.Default), ((await Levy(owner)).ValidUntil, (await Levy(owner)).ValidUntilSource));

        await SetValidUntil(owner, null);
        await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", request with { RecipientName = "ГУК Київ" }, Json);

        Assert.Null((await Levy(owner)).ValidUntil);
    }

    private WebApplicationFactory<Program> AppOn(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new Features.Fx.FakeTime(new DateTimeOffset(today, new TimeOnly(10, 0), TimeSpan.Zero)))));

    private static async Task<HttpClient> SignInEmpty(WebApplicationFactory<Program> app)
    {
        var client = await ApiFixture.SignIn(app, ApiFixture.AllowedEmail);
        await Restore(client, Empty);
        return client;
    }

    private static async Task Restore(HttpClient client, string file)
    {
        var response = await client.PostAsync("/api/restore", new StringContent(file, Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // Learned from a payment in the temporary period, as the bank sync would.
    private async Task SeedLearned(PaymentKind kind)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id).SingleAsync();
        database.TreasuryAccounts.Add(new TreasuryAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            LearnedIban = LearnedIban,
            LearnedRecipientName = "ГУК у м.Києві",
            LearnedRecipientCode = "37993783",
            LearnedExternalId = $"op-{kind}",
            LearnedPaidOn = new DateOnly(2026, 8, 3),
            LearnedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();
    }

    private static async Task SetValidUntil(HttpClient owner, DateOnly? validUntil)
    {
        var response = await owner.PutAsJsonAsync(
            "/api/settings/treasury-accounts/MilitaryLevy/valid-until", new TreasuryAccountValidUntilRequest(validUntil), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<TreasuryAccountResponse[]> Accounts(HttpClient owner) =>
        (await owner.GetFromJsonAsync<TreasuryAccountResponse[]>("/api/settings/treasury-accounts", Json))!;

    private static async Task<TreasuryAccountResponse> Levy(HttpClient owner) =>
        (await Accounts(owner)).Single(account => account.Kind == PaymentKind.MilitaryLevy);

    private static async Task<JsonArray> ExpiredOnDashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonObject>("/api/dashboard"))!["expiredTreasuryAccounts"]!.AsArray();

    private static async Task<PaymentDetailsResponse> Details(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/payment-details?{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentDetailsResponse>(Json))!;
    }
}
