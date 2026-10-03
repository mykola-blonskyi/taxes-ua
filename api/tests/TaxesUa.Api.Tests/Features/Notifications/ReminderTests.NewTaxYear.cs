using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;
using TaxesUa.Api.Features.TaxYears;

namespace TaxesUa.Api.Tests.Features.Notifications;

// In December the owner is told once to prepare the coming tax year, through the reminder channels and the
// dashboard (Rule 9). Tax years are shared by every owner, so each test names years of its own.
public sealed partial class ReminderTests
{
    private const string NewYearText =
        "Податковий {0} рік: перевірте й підтвердьте параметри.\n"
        + "Мінімальна зарплата, база й ставка ЄСВ, ліміти. Можна клонувати попередній рік і виправити цифри.";

    private static readonly TaxYearConfigRequest Parameters =
        new(800_000, 500, 100, 2_200, 1_500, 1_000, [85], 19, 40, 10, 15, 10, [], "a test source");

    [Fact]
    public async Task Nothing_is_said_in_November_even_when_next_year_is_missing()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2041, 11, 30), 12, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await ConfigureYear(owner, 2041);

        await Run(application);
        clock.SetUtcNow(Kyiv(new DateOnly(2041, 11, 30), 23, 59));
        await Run(application);

        Assert.Empty(Texts(telegram));
        Assert.Empty(await SentLog(application));
        Assert.Null((await Dashboard(owner))["newTaxYear"]);
    }

    [Fact]
    public async Task A_missing_next_year_in_December_is_told_once_from_nine_and_shown_on_the_dashboard()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2043, 12, 1), 8, 59));
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.UseSetting("App:PublicUrl", "https://taxes.example.com"));
        var owner = await Prepare(application, telegram);
        await ConfigureYear(owner, 2043);

        await Run(application);
        Assert.Empty(Texts(telegram));
        var card = (await Dashboard(owner))["newTaxYear"]!;
        Assert.Equal(2044, card["year"]!.GetValue<int>());
        Assert.Equal("Missing", card["state"]!.GetValue<string>());

        clock.SetUtcNow(Kyiv(new DateOnly(2043, 12, 1), 9, 0));
        await Run(application);
        await Run(application);
        clock.SetUtcNow(Kyiv(new DateOnly(2043, 12, 1), 20, 0));
        await Run(application);

        var sent = Assert.Single(telegram.To("sendMessage"));
        Assert.Equal(
            string.Format(NewYearText, 2044) + "\nВідкрити налаштування: https://taxes.example.com/settings?tab=taxYears",
            sent.Body["text"]!.GetValue<string>());
        var claim = Assert.Single(await SentLog(application));
        Assert.Equal("NewTaxYear:2044", claim.Incident);
        Assert.NotNull(claim.DeliveredAt);
        Assert.Equal("Missing", (await Dashboard(owner))["newTaxYear"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_cloned_but_unconfirmed_year_is_told_once_and_confirming_clears_the_card()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2045, 12, 10), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2045", Parameters, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/tax-years/2045/clone-to/2046", null)).StatusCode);

        await Run(application);
        await Run(application);

        Assert.Equal([string.Format(NewYearText, 2046)], Texts(telegram));
        Assert.Equal("Unconfirmed", (await Dashboard(owner))["newTaxYear"]!["state"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/tax-years/2046/verify", null)).StatusCode);
        Assert.Null((await Dashboard(owner))["newTaxYear"]);
    }

    [Fact]
    public async Task A_confirmed_next_year_is_neither_told_nor_shown()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2047, 12, 10), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await ConfigureYear(owner, 2047);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2048", Parameters, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/tax-years/2048/verify", null)).StatusCode);

        await Run(application);

        Assert.Empty(Texts(telegram));
        Assert.Empty(await SentLog(application));
        Assert.Null((await Dashboard(owner))["newTaxYear"]);
    }

    [Fact]
    public async Task The_next_December_is_a_new_year_and_is_told_again()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2051, 12, 5), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await ConfigureYear(owner, 2051);

        await Run(application);
        await ConfigureYear(owner, 2052, verify: false);
        clock.SetUtcNow(Kyiv(new DateOnly(2052, 12, 5), 10, 0));
        await Run(application);

        Assert.Equal([string.Format(NewYearText, 2052), string.Format(NewYearText, 2053)], Texts(telegram));
    }

    [Fact]
    public async Task A_year_still_missing_on_the_first_of_January_hands_the_alert_to_the_missing_year_incident_once()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2057, 12, 31), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await ConfigureYear(owner, 2057);

        await Run(application);
        Assert.Equal([string.Format(NewYearText, 2058)], Texts(telegram));

        clock.SetUtcNow(Kyiv(new DateOnly(2058, 1, 1), 10, 0));
        await Run(application);
        await Run(application);
        clock.SetUtcNow(Kyiv(new DateOnly(2058, 1, 1), 18, 0));
        await Run(application);

        Assert.Equal([string.Format(NewYearText, 2058), string.Format(MissingYearText, 2058)], Texts(telegram));
        Assert.Equal(
            ["MissingTaxYear:2058", "NewTaxYear:2058"],
            (await SentLog(application)).Select(claim => claim.Incident).Order().ToArray());
        var step = (await Dashboard(owner))["nextStep"]!;
        Assert.Equal("MissingTaxYear", step["state"]!.GetValue<string>());
        Assert.Equal(2058, step["missingTaxYear"]!.GetValue<int>());
        Assert.Null((await Dashboard(owner))["newTaxYear"]);

        await ConfigureYear(owner, 2058, verify: false);
        clock.SetUtcNow(Kyiv(new DateOnly(2058, 1, 2), 10, 0));
        await Run(application);

        Assert.Equal(2, Texts(telegram).Count);
    }

    [Fact]
    public async Task A_missing_current_year_is_told_from_nine_in_any_month_and_not_once_configured()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(new DateOnly(2056, 6, 10), 8, 59));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);

        await Run(application);
        Assert.Empty(Texts(telegram));

        clock.SetUtcNow(Kyiv(new DateOnly(2056, 6, 10), 9, 0));
        await Run(application);
        await Run(application);
        Assert.Equal([string.Format(MissingYearText, 2056)], Texts(telegram));

        await ConfigureYear(owner, 2056);
        clock.SetUtcNow(Kyiv(new DateOnly(2056, 6, 11), 9, 0));
        await Run(application);
        Assert.Single(Texts(telegram));
    }

    private const string MissingYearText =
        "Параметри {0} року не задано, баланс недоступний.\n"
        + "Клонуйте попередній рік на вкладці податкових років, виправте цифри й позначте рік перевіреним.";

    private static async Task ConfigureYear(HttpClient owner, int year, bool verify = true)
    {
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", Parameters, Json)).StatusCode);
        if (verify)
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/tax-years/{year}/verify", null)).StatusCode);
        }
    }

    private static async Task<JsonObject> Dashboard(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonObject>("/api/dashboard"))!;
}
