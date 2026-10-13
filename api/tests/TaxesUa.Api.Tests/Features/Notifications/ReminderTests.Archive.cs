using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Archive;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using static TaxesUa.Api.Tests.Features.Notifications.EmailChannelTests;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

// Once every group 3 declaration of a year is marked filed, the owner is told once per channel to download
// the year's archive, and the dashboard shows the notice until the ZIP is downloaded or the notice dismissed
// (#287, Rules 17 and 19). Prepare's 2031 has four group 3 quarters; the clock stands in February 2032,
// after the fourth quarter's declaration.
public sealed partial class ReminderTests
{
    private static readonly DateTimeOffset YearFiled = Kyiv(new DateOnly(2032, 2, 5), 10, 0);

    // The seeded suspension is open, so the date is the earliest possible: the fourth quarter's filing date,
    // 2032-02-02, plus 1095 days.
    private const string ArchiveText =
        "Усі декларації за 2031 рік подано: завантажте архів року.\n"
        + "Документи за 2031 рік зберігати не менше ніж до 01.02.2035. Строк продовжено на час воєнного стану: "
        + "після його припинення або скасування ще 1095 днів.\n"
        + "Відкрити архів: https://taxes.example.com/archive?year=2031";

    [Fact]
    public async Task The_last_mark_of_the_year_is_told_once_per_channel_and_marking_again_sends_nothing()
    {
        var telegram = new StubTelegramHandler();
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(YearFiled);
        await using var application = fixture.CreateApplication(
            email, clock, configure: builder =>
            {
                telegram.Clock = clock;
                builder.UseSetting("Telegram:BotToken", ApiFixture.TelegramTestToken);
                builder.UseSetting("App:PublicUrl", "https://taxes.example.com");
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient<TelegramClient>().ConfigurePrimaryHttpMessageHandler(() => telegram));
            });
        var owner = await Prepare(application, telegram);
        await Connected(owner, email, MailAddress);

        await MarkQuarters(owner, 1, 2, 3);
        await Run(application);
        Assert.Empty(ArchiveTexts(telegram));
        Assert.Null((await Dashboard(owner))["archive"]);

        await MarkQuarters(owner, 4);
        await Task.WhenAll(Run(application), Run(application));
        await MarkQuarters(owner, 4);
        await Run(application);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/declarations/2031/4/filing")).StatusCode);
        await MarkQuarters(owner, 4);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Run(application);

        Assert.Equal([ArchiveText], ArchiveTexts(telegram));
        var mail = Assert.Single(email.Delivered, message => message.Subject.StartsWith("Усі декларації", StringComparison.Ordinal));
        Assert.Equal(ArchiveText, mail.Text);
        var claims = (await SentLog(application)).Where(claim => claim.Incident == "Archive:2031").ToList();
        Assert.Equal([NotificationChannelKind.Telegram, NotificationChannelKind.Email], claims.Select(claim => claim.Channel).Order());
        Assert.All(claims, claim => Assert.NotNull(claim.DeliveredAt));
    }

    [Fact]
    public async Task A_restart_finds_the_years_claim_and_sends_nothing_more()
    {
        var telegram = new StubTelegramHandler();
        await using (var application = fixture.CreateApplication(telegram, new FakeTimeProvider(YearFiled)))
        {
            var owner = await Prepare(application, telegram);
            await MarkQuarters(owner, 1, 2, 3, 4);
            await Run(application);
        }

        var afterRestart = new StubTelegramHandler();
        await using var restarted = fixture.CreateApplication(afterRestart, new FakeTimeProvider(YearFiled + TimeSpan.FromHours(1)));
        await Run(restarted);

        Assert.Single(ArchiveTexts(telegram));
        Assert.Empty(ArchiveTexts(afterRestart));
        Assert.Single(await SentLog(restarted), claim => claim.Incident == "Archive:2031");
    }

    [Fact]
    public async Task A_year_finished_more_than_14_days_before_a_run_is_shown_but_not_sent()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(YearFiled);
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.ConfigureTestServices(ApiFixture.KeepSessionAcrossClockJumps));
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3, 4);

        clock.Advance(ArchiveReminder.MessageWindow + TimeSpan.FromMinutes(1));
        await Run(application);

        Assert.Empty(ArchiveTexts(telegram));
        Assert.DoesNotContain(await SentLog(application), claim => claim.Incident == "Archive:2031");
        Assert.Equal(2031, (await Dashboard(owner))["archive"]!["year"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_year_finished_14_days_before_a_run_is_still_sent()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(YearFiled);
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.ConfigureTestServices(ApiFixture.KeepSessionAcrossClockJumps));
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3, 4);

        clock.Advance(ArchiveReminder.MessageWindow);
        await Run(application);

        Assert.Single(ArchiveTexts(telegram));
    }

    [Fact]
    public async Task The_reminder_follows_the_owners_language()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(YearFiled));
        var owner = await Prepare(application, telegram, locale: "ru");
        await MarkQuarters(owner, 1, 2, 3, 4);

        await Run(application);

        Assert.Equal(
            [
                "Все декларации за 2031 год поданы: скачайте архив года.\n"
                + "Документы за 2031 год хранить не менее чем до 01.02.2035. Срок продлён на время военного положения: "
                + "после его прекращения или отмены ещё 1095 дней.",
            ],
            Texts(telegram).Where(text => text.StartsWith("Все декларации", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_dashboard_notice_names_the_year_and_its_date_and_a_download_clears_it_and_the_reminder()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(YearFiled));
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3, 4);

        var notice = (await Dashboard(owner))["archive"]!;
        Assert.Equal(2031, notice["year"]!.GetValue<int>());
        Assert.Equal("ExtendedWhileSuspended", notice["keepUntil"]!["state"]!.GetValue<string>());
        Assert.Equal("2035-02-01", notice["keepUntil"]!["date"]!.GetValue<string>());

        await DownloadZip(owner, 2031);
        await Run(application);

        Assert.Null((await Dashboard(owner))["archive"]);
        Assert.Empty(ArchiveTexts(telegram));
    }

    [Fact]
    public async Task A_dismissal_clears_the_notice_for_its_owner_only()
    {
        var telegram = new StubTelegramHandler();
        var otherEmail = fixture.NewOwner();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(YearFiled));
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3, 4);
        var other = await ApiFixture.SignIn(application, otherEmail);

        Assert.Null((await Dashboard(other))["archive"]);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync("/api/archive/2031/notice/dismiss", null)).StatusCode);
        Assert.NotNull((await Dashboard(owner))["archive"]);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/archive/2031/notice/dismiss", null)).StatusCode);

        Assert.Null((await Dashboard(owner))["archive"]);
    }

    [Fact]
    public async Task A_zip_taken_before_the_last_mark_leaves_the_notice_up()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(YearFiled);
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3);
        await DownloadZip(owner, 2031);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/archive/2031/notice/dismiss", null)).StatusCode);

        clock.Advance(TimeSpan.FromMinutes(1));
        await MarkQuarters(owner, 4);

        Assert.Equal(2031, (await Dashboard(owner))["archive"]!["year"]!.GetValue<int>());
    }

    [Fact]
    public async Task Editing_a_mark_after_the_answer_keeps_the_year_answered()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(YearFiled);
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await MarkQuarters(owner, 1, 2, 3, 4);
        await DownloadZip(owner, 2031);

        clock.Advance(TimeSpan.FromMinutes(1));
        var edited = await owner.PutAsJsonAsync(
            "/api/declarations/2031/4/filing",
            new DeclarationFilingRequest(new DateOnly(2032, 2, 3), DeclarationType.Reporting), Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Null((await Dashboard(owner))["archive"]);
    }

    [Fact]
    public async Task With_two_pending_years_the_newest_shows_and_the_older_takes_its_place_once_it_is_answered()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(YearFiled));
        var owner = await Prepare(application, telegram);
        var taxYear = new TaxYearConfigRequest(800_000, 500, 100, 2_200, 1_500, 1_000, [85], 19, 40, 10, 15, 10, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2030", taxYear, Json)).StatusCode);
        await SetSettings(owner, new DateOnly(2030, 1, 1), "uk", esvExempt: true);
        var income = new TransactionRequest(
            new DateOnly(2030, 2, 10), 12_345_600, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", income, Json)).StatusCode);
        await MarkQuartersOf(owner, 2030, 1, 2, 3, 4);
        await MarkQuartersOf(owner, 2031, 1, 2, 3, 4);

        Assert.Equal(2031, (await Dashboard(owner))["archive"]!["year"]!.GetValue<int>());

        await DownloadZip(owner, 2031);

        Assert.Equal(2030, (await Dashboard(owner))["archive"]!["year"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/archive/2030/notice/dismiss", null)).StatusCode);

        Assert.Null((await Dashboard(owner))["archive"]);
    }

    [Fact]
    public async Task The_dismissal_needs_a_session_and_a_year_in_range()
    {
        await using var application = fixture.CreateApplication(new StubTelegramHandler());
        var owner = await ApiFixture.SignIn(application, _ownerEmail);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ApiFixture.CreateClient(application).PostAsync("/api/archive/2031/notice/dismiss", null)).StatusCode);
        var outOfRange = await owner.PostAsync("/api/archive/1999/notice/dismiss", null);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        ProblemAssert.FieldIs(await ProblemAssert.ReadAsync(outOfRange), "year", "year_out_of_range");
    }

    private static Task MarkQuarters(HttpClient owner, params int[] quarters) => MarkQuartersOf(owner, 2031, quarters);

    private static async Task MarkQuartersOf(HttpClient owner, int year, params int[] quarters)
    {
        foreach (var quarter in quarters)
        {
            var filedOn = quarter == 4 ? new DateOnly(year + 1, 2, 2) : DeclarationsEndpoints.QuarterEnd(year, quarter).AddDays(10);
            var response = await owner.PutAsJsonAsync(
                $"/api/declarations/{year}/{quarter}/filing", new DeclarationFilingRequest(filedOn, DeclarationType.Reporting), Json);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task DownloadZip(HttpClient owner, int year)
    {
        using var response = await owner.GetAsync($"/api/archive/{year}/zip");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    private static List<string> ArchiveTexts(StubTelegramHandler telegram) =>
        [.. Texts(telegram).Where(text => text.StartsWith("Усі декларації", StringComparison.Ordinal))];
}
