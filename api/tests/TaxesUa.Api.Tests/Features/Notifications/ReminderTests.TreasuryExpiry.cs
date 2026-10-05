using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Payments;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

// An account in use whose end has passed is told once per channel, from 09:00 Kyiv on the first working day after
// the end (#261, Rule 16). No test in this class writes the 2026 tax year, so the seeded 2026-12-31 applies.
public sealed partial class ReminderTests
{
    private const string LevyAccountText =
        "Рахунок Казначейства для «Військовий збір» закрито з 01.01.2027.\n"
        + "Введіть новий рахунок з Електронного кабінету в налаштуваннях. Доти застосунок не дає реквізитів для оплати.";

    private static readonly TreasuryAccountRequest LevyAccount = new("UA358999980333159998000026011", "ГУК у м.Києві", "37993783");

    [Fact]
    public async Task The_levy_account_ending_by_default_on_2026_12_31_is_told_once_from_nine_on_the_first_of_january()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2026, 12, 31), 10, 0));
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.UseSetting("App:PublicUrl", "https://taxes.example.com"));
        var owner = await PrepareLinked(application, telegram);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", LevyAccount, Json)).StatusCode);

        await Run(application);
        Assert.Empty(TreasuryTexts(telegram));
        Assert.Empty(await ExpiredAccounts(owner));

        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 1), 8, 59));
        await Run(application);
        Assert.Empty(TreasuryTexts(telegram));

        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 1), 9, 0));
        await Run(application);
        await Run(application);
        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 4), 10, 0));
        await Run(application);

        Assert.Equal(
            [LevyAccountText + "\nВідкрити налаштування: https://taxes.example.com/settings?tab=treasury"],
            TreasuryTexts(telegram));
        var claim = Assert.Single(await SentLog(application), claim => claim.Incident.StartsWith("TreasuryAccountExpired", StringComparison.Ordinal));
        Assert.Equal("TreasuryAccountExpired:MilitaryLevy:2026-12-31", claim.Incident);
        Assert.NotNull(claim.DeliveredAt);
        var expired = Assert.Single(await ExpiredAccounts(owner));
        Assert.Equal("2026-12-31", expired!["validUntil"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_end_on_a_friday_is_told_on_monday_and_a_removed_end_is_never_told()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2027, 1, 5), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await PrepareLinked(application, telegram);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/Esv", LevyAccount with { ValidUntil = new DateOnly(2027, 1, 8) }, Json)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy", LevyAccount with { ValidUntil = new DateOnly(2027, 1, 8) }, Json)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/settings/treasury-accounts/MilitaryLevy/valid-until", new TreasuryAccountValidUntilRequest(null), Json)).StatusCode);

        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 9), 10, 0));
        await Run(application);
        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 11), 8, 59));
        await Run(application);
        Assert.Empty(TreasuryTexts(telegram));

        clock.SetUtcNow(Kyiv(new DateOnly(2027, 1, 11), 9, 0));
        await Run(application);

        Assert.Equal(
            ["Рахунок Казначейства для «ЄСВ» закрито з 09.01.2027."],
            TreasuryTexts(telegram).Select(text => text.Split('\n')[0]));
    }

    private static List<string> TreasuryTexts(StubTelegramHandler telegram) =>
        [.. Texts(telegram).Where(text => text.StartsWith("Рахунок Казначейства", StringComparison.Ordinal))];

    private static async Task<JsonArray> ExpiredAccounts(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonObject>("/api/dashboard"))!["expiredTreasuryAccounts"]!.AsArray();

    // Prepare's income is dated 2031, which a clock in 2026 refuses as a future receipt; an expired account
    // needs no income, only a linked channel.
    private async Task<HttpClient> PrepareLinked(WebApplicationFactory<Program> application, StubTelegramHandler telegram)
    {
        var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await OwnerId(database);
            await database.NotificationChannels.Where(row => row.UserId != userId).ExecuteDeleteAsync();
            await ForgetPollOffset(scope.ServiceProvider);
        }

        await Link(application, owner, telegram);
        telegram.ClearCalls();
        return owner;
    }
}
