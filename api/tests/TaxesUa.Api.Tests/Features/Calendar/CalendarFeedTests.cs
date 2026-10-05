using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Api.Tests.Features.Fx;
using TaxesUa.Api.Tests.Features.Notifications;
using TaxesUa.Engine;
using IcsCalendar = Ical.Net.Calendar;

namespace TaxesUa.Api.Tests.Features.Calendar;

// The fixture owns its database, so a test only needs years of its own for the tax year rows it writes.
// The clock reads mid-June of a year, so the feed covers that year and the next.
public sealed partial class CalendarFeedTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DayOfWeek[] Weekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    [GeneratedRegex("^/api/calendar/feed/[0-9a-f]{64}\\.ics$")]
    private static partial Regex FeedPath();

    [Fact]
    public async Task The_feed_matches_the_engines_shifted_deadlines_for_two_years_and_carries_no_amount()
    {
        const int year = 2090;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        await PostIncome(owner, new DateOnly(year, 2, 10), 1_234_567);
        await PostPayment(owner, new DateOnly(year, 4, 10), PaymentKind.Esv, 98_765, year, quarter: 1);

        var ics = await Subscribe(application, owner);

        var expected = new Dictionary<string, DateOnly>();
        var shifted = 0;
        foreach (var each in new[] { year, year + 1 })
        {
            var config = Config(each);
            var settings = Engine(new DateOnly(year, 1, 1));
            foreach (var quarter in Enumerable.Range(1, 4))
            {
                var due = DeadlineCalendar.ForQuarter(each, quarter, config, settings);
                expected[$"esv-{each}-q{quarter}@taxes-ua"] = due.Esv.Due;
                expected[$"taxpayment-{each}-q{quarter}@taxes-ua"] = due.TaxPayment.Due;
                expected[$"declaration-{each}-q{quarter}@taxes-ua"] = due.Declaration.Due;
                shifted += new[] { due.Esv, due.TaxPayment, due.Declaration }.Count(deadline => deadline.Due != deadline.Statutory);
            }
        }

        Assert.True(shifted >= 4, $"the fixture must exercise shifting, but only {shifted} dates moved");
        var calendar = Parse(ics);
        Assert.Equal(expected.Count, calendar.Events.Count);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            calendar.Events.ToDictionary(each => each.Uid!, DayOf).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        foreach (var each in calendar.Events)
        {
            Assert.False(each.DtStart!.HasTime);
            Assert.Equal(DayOf(each).AddDays(1), DateOnly.FromDateTime(each.DtEnd!.Value));
            Assert.Equal(["-P6DT15H", "-PT15H"], AlarmTriggers(ics, each.Uid!));
            Assert.Equal(2, each.Alarms.Count);
        }

        Assert.Equal("Сплата ЄСВ · Q1 2090", calendar.Events.Single(each => each.Uid == "esv-2090-q1@taxes-ua").Summary);
        Assert.Equal("Сплата ЄП і ВЗ · Q4 2091", calendar.Events.Single(each => each.Uid == "taxpayment-2091-q4@taxes-ua").Summary);
        Assert.Equal("Подання декларації · Q2 2090", calendar.Events.Single(each => each.Uid == "declaration-2090-q2@taxes-ua").Summary);
        foreach (var amount in new[] { "1234567", "12345", "12 345", "98765", "987,65", "987.65" })
        {
            Assert.DoesNotContain(amount, ics);
        }
    }

    [Fact]
    public async Task Monthly_advance_mode_adds_each_advance_on_the_date_the_periods_screen_shows()
    {
        const int year = 2088;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 3, 10), PaymentMode.MonthlyAdvance);

        var calendar = Parse(await Subscribe(application, owner));

        var expected = new Dictionary<string, DateOnly>();
        foreach (var each in new[] { year, year + 1 })
        {
            var periods = (await owner.GetFromJsonAsync<JsonObject>($"/api/periods/{each}", Json))!;
            foreach (var month in periods["months"]!.AsArray())
            {
                expected[$"advance-{each}-m{month!["month"]!.GetValue<int>():00}@taxes-ua"] =
                    DateOnly.Parse(month["recommendedDate"]!.GetValue<string>());
            }
        }

        var advances = calendar.Events.Where(each => each.Uid!.StartsWith("advance-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(22, expected.Count);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            advances.ToDictionary(each => each.Uid!, DayOf).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(new DateOnly(year + 1, 1, 15), DayOf(advances.Single(each => each.Uid == $"advance-{year}-m12@taxes-ua")));
        Assert.Equal("Аванс ЄП, ВЗ і ЄСВ · березень 2088", advances.Single(each => each.Uid == $"advance-{year}-m03@taxes-ua").Summary);
        Assert.Equal(24, calendar.Events.Count - advances.Length);
    }

    [Fact]
    public async Task Nothing_is_listed_for_the_quarters_after_group_3_ended_until_the_owner_is_back()
    {
        const int year = 2094;
        await using var application = At(new FakeClock(new DateTimeOffset(year, 10, 5, 9, 0, 0, TimeSpan.Zero)));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly, incomeLimitMinWages: 1);
        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 8, 10), 300_000);

        var stopped = Parse(await Subscribe(application, owner));

        Assert.Equal(
            [(year, 1), (year, 2)],
            stopped.Events.Select(each => QuarterOf(each.Uid!)).Distinct().OrderBy(each => each));
        Assert.Equal(6, stopped.Events.Count);
        Assert.Contains(stopped.Events, each => each.Uid == $"declaration-{year}-q2@taxes-ua");

        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly, incomeLimitMinWages: 1, backOnGroup3From: new YearQuarter(year + 1, 2));
        var resumed = Parse(await Subscribe(application, owner));

        Assert.Equal(
            [(year, 1), (year, 2), (year + 1, 2), (year + 1, 3), (year + 1, 4)],
            resumed.Events.Select(each => QuarterOf(each.Uid!)).Distinct().OrderBy(each => each));
    }

    [Fact]
    public async Task A_wrong_secret_is_404_and_the_document_is_not_in_the_openapi_description()
    {
        const int year = 2086;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        var path = await Rotate(owner);
        using var anonymous = ApiFixture.CreateClient(application);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);
        var secret = path["/api/calendar/feed/".Length..^".ics".Length];
        foreach (var wrong in new[]
        {
            "/api/calendar/feed/" + new string('0', 64) + ".ics",
            "/api/calendar/feed/" + secret.ToUpperInvariant() + ".ics",
            "/api/calendar/feed/" + secret[..63] + ".ics",
            "/api/calendar/feed/" + secret,
            "/api/calendar/feed/not-a-secret.ics",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(wrong)).StatusCode);
        }

        var openApi = await anonymous.GetStringAsync("/api/openapi/v1.json");
        Assert.DoesNotContain("/api/calendar/feed/{secret}", openApi);
        Assert.Contains("/api/calendar/feed/rotate", openApi);
    }

    [Fact]
    public async Task Rotating_creates_the_first_secret_and_then_invalidates_the_old_url()
    {
        const int year = 2084;
        const string newcomer = "newcomer@example.com";
        await using var application = fixture.CreateApplication(builder =>
        {
            builder.UseSetting("Auth:AllowedEmails", newcomer);
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero))));
        });
        using var owner = await ApiFixture.SignIn(application, newcomer);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        using var anonymous = ApiFixture.CreateClient(application);

        Assert.Null((await owner.GetFromJsonAsync<JsonObject>("/api/calendar/feed", Json))!["createdAt"]);

        var first = await Rotate(owner);
        Assert.Matches(FeedPath(), first);
        var fetched = await anonymous.GetAsync(first);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("text/calendar; charset=utf-8", fetched.Content.Headers.ContentType!.ToString());
        Assert.Equal("no-store", fetched.Headers.CacheControl!.ToString());
        Assert.Equal(
            new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero),
            (await owner.GetFromJsonAsync<JsonObject>("/api/calendar/feed", Json))!["createdAt"]!.GetValue<DateTimeOffset>());

        var second = await Rotate(owner);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(second)).StatusCode);
    }

    [Fact]
    public async Task Only_the_rotation_shows_the_url_and_the_database_keeps_only_its_hash()
    {
        const int year = 2083;
        var email = fixture.NewOwner();
        await using var scoped = fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTime(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero)))));
        using var owner = await ApiFixture.SignIn(scoped, email);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);

        var rotated = await owner.PostAsync("/api/calendar/feed/rotate", null);
        var body = (await rotated.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        var path = body["path"]!.GetValue<string>();
        var secret = path["/api/calendar/feed/".Length..^".ics".Length];
        Assert.Equal(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero), body["createdAt"]!.GetValue<DateTimeOffset>());

        var read = await owner.GetStringAsync("/api/calendar/feed");
        Assert.DoesNotContain(secret, read);
        Assert.DoesNotContain("path", read);

        await using var scope = scoped.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        var stored = await database.CalendarFeeds.AsNoTracking().SingleAsync(feed => feed.UserId == user.Id);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            stored.SecretHash);
        using var anonymous = ApiFixture.CreateClient(scoped);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/calendar/feed/" + stored.SecretHash + ".ics")).StatusCode);
    }

    [Fact]
    public async Task Each_owner_has_their_own_secret_and_sees_only_their_own_deadlines()
    {
        const int year = 2082;
        await using var application = At(year);
        using var first = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var second = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(first, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        await SetUp(second, year, new DateOnly(year, 7, 1), PaymentMode.MonthlyAdvance);

        var firstPath = await Rotate(first);
        var secondPath = await Rotate(second);
        using var anonymous = ApiFixture.CreateClient(application);
        var firstUids = Parse(await anonymous.GetStringAsync(firstPath)).Events.Select(each => each.Uid!).ToHashSet();
        var secondUids = Parse(await anonymous.GetStringAsync(secondPath)).Events.Select(each => each.Uid!).ToHashSet();

        Assert.NotEqual(firstPath, secondPath);
        Assert.Contains($"esv-{year}-q1@taxes-ua", firstUids);
        Assert.DoesNotContain(firstUids, uid => uid.StartsWith("advance-", StringComparison.Ordinal));
        Assert.DoesNotContain($"esv-{year}-q1@taxes-ua", secondUids);
        Assert.Contains($"advance-{year}-m07@taxes-ua", secondUids);
        Assert.Contains($"esv-{year}-q3@taxes-ua", secondUids);

        await Rotate(second);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(firstPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(secondPath)).StatusCode);
    }

    [Fact]
    public async Task Events_keep_their_uid_across_fetches_rotations_and_payments_and_the_download_is_the_same_document()
    {
        const int year = 2080;
        var clock = new FakeClock(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero));
        await using var application = At(clock);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        using var anonymous = ApiFixture.CreateClient(application);

        var path = await Rotate(owner);
        var before = Dated(Parse(await anonymous.GetStringAsync(path)));
        clock.Set(new DateTimeOffset(year, 6, 16, 9, 0, 0, TimeSpan.Zero));
        await PostIncome(owner, new DateOnly(year, 2, 10), 700_000);
        await PostPayment(owner, new DateOnly(year, 4, 10), PaymentKind.Esv, 50_000, year, quarter: 1);
        var afterPayment = Dated(Parse(await anonymous.GetStringAsync(path)));
        var rotated = Dated(Parse(await anonymous.GetStringAsync(await Rotate(owner))));

        Assert.Equal(before, afterPayment);
        Assert.Equal(before, rotated);

        var download = await owner.GetAsync("/api/calendar/deadlines.ics");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("taxes-ua-deadlines.ics", download.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("text/calendar", download.Content.Headers.ContentType!.ToString());
        Assert.Equal(before, Dated(Parse(await download.Content.ReadAsStringAsync())));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/calendar/deadlines.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/calendar/feed/rotate", null)).StatusCode);
    }

    [Fact]
    public async Task In_January_the_previous_years_fourth_quarter_and_december_advance_are_still_listed()
    {
        const int year = 2070;
        await using var application = At(new FakeClock(new DateTimeOffset(year, 1, 15, 9, 0, 0, TimeSpan.Zero)));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year - 1, new DateOnly(year - 1, 1, 1), PaymentMode.MonthlyAdvance, years: [year - 1, year, year + 1]);

        var calendar = Parse(await Subscribe(application, owner));

        var previous = calendar.Events.Where(each => each.Uid!.Contains($"-{year - 1}-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [$"advance-{year - 1}-m12@taxes-ua", $"declaration-{year - 1}-q4@taxes-ua", $"esv-{year - 1}-q4@taxes-ua", $"taxpayment-{year - 1}-q4@taxes-ua"],
            previous.Select(each => each.Uid!).OrderBy(each => each, StringComparer.Ordinal));
        Assert.Equal(new DateOnly(year, 1, 15), DayOf(previous.Single(each => each.Uid!.StartsWith("advance-", StringComparison.Ordinal))));
        Assert.All(calendar.Events, each => Assert.True(DayOf(each).Year >= year));
        Assert.Contains(calendar.Events, each => each.Uid == $"esv-{year}-q1@taxes-ua");
        Assert.Contains(calendar.Events, each => each.Uid == $"advance-{year + 1}-m12@taxes-ua");
    }

    [Fact]
    public async Task An_esv_exempt_owner_has_no_esv_events()
    {
        const int year = 2068;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly, esvExempt: true);

        var calendar = Parse(await Subscribe(application, owner));

        Assert.DoesNotContain(calendar.Events, each => each.Uid!.StartsWith("esv-", StringComparison.Ordinal));
        Assert.Equal(16, calendar.Events.Count);
    }

    [Fact]
    public async Task A_quarter_before_group_3_starts_lists_its_esv_and_nothing_else()
    {
        const int year = 2069;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 8, 10), PaymentMode.Quarterly);
        var status = new DpsStatusRequest(new DateOnly(year, 10, 1), null, false, false, false);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/dps-status", status, Json)).StatusCode);

        var uids = Parse(await Subscribe(application, owner)).Events.Select(each => each.Uid!).ToArray();
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/settings/dps-status", status with { Group3Since = null }, Json)).StatusCode);

        Assert.Contains($"esv-{year}-q3@taxes-ua", uids);
        Assert.DoesNotContain($"taxpayment-{year}-q3@taxes-ua", uids);
        Assert.DoesNotContain($"declaration-{year}-q3@taxes-ua", uids);
        Assert.Contains($"declaration-{year}-q4@taxes-ua", uids);
    }

    [Fact]
    public async Task A_year_outside_the_ledger_gets_no_advances()
    {
        const int year = 2066;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        // Year 2066 has no tax year row, so 2067 lies past the gap and the periods screen shows no advances for it.
        await SetUp(owner, year - 1, new DateOnly(year - 1, 1, 1), PaymentMode.MonthlyAdvance, years: [year - 1, year + 1]);

        var calendar = Parse(await Subscribe(application, owner));

        Assert.Equal([$"advance-{year - 1}-m12@taxes-ua"], calendar.Events.Where(each => each.Uid!.StartsWith("advance-", StringComparison.Ordinal)).Select(each => each.Uid!));
    }

    [Fact]
    public async Task Uids_carry_a_short_owner_key_that_differs_between_owners_and_survives_rotation()
    {
        const int year = 2064;
        await using var application = At(year);
        using var first = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var second = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(first, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        await SetUp(second, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        using var anonymous = ApiFixture.CreateClient(application);

        var firstPath = await Rotate(first);
        var firstUids = RawUids(await anonymous.GetStringAsync(firstPath));
        var rotatedUids = RawUids(await anonymous.GetStringAsync(await Rotate(first)));
        var secondUids = RawUids(await anonymous.GetStringAsync(await Rotate(second)));

        Assert.All(firstUids, uid => Assert.Matches("^[a-z]+-\\d{4}-(q\\d|m\\d{2})-[0-9a-f]{8}@taxes-ua$", uid));
        Assert.Equal(firstUids, rotatedUids);
        Assert.Empty(firstUids.Intersect(secondUids));
        Assert.Equal(firstUids.Count, secondUids.Count);
    }

    private static HashSet<string> RawUids(string ics) =>
        [.. Regex.Matches(Regex.Replace(ics, "\r\n ", string.Empty), @"UID:(\S+)").Select(match => match.Groups[1].Value)];

    [Fact]
    public async Task The_download_needs_no_subscription_and_the_summaries_follow_the_owners_locale()
    {
        const int year = 2078;
        await using var application = At(year);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.MonthlyAdvance, locale: "ru");

        var ics = await owner.GetStringAsync("/api/calendar/deadlines.ics");

        var summaries = Parse(ics).Events.ToDictionary(each => each.Uid!, each => each.Summary);
        Assert.Equal($"Уплата ЕСВ · Q1 {year}", summaries[$"esv-{year}-q1@taxes-ua"]);
        Assert.Equal($"Уплата ЕН и ВС · Q1 {year}", summaries[$"taxpayment-{year}-q1@taxes-ua"]);
        Assert.Equal($"Подача декларации · Q1 {year}", summaries[$"declaration-{year}-q1@taxes-ua"]);
        Assert.Equal($"Аванс ЕН, ВС и ЕСВ · май {year}", summaries[$"advance-{year}-m05@taxes-ua"]);
    }

    [Fact]
    public async Task The_secret_reaches_no_log_no_backup_and_no_change_history()
    {
        const int year = 2076;
        var logs = new CapturedLogs();
        await using var application = fixture.CreateApplication(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(logs);
            });
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero))));
        });
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, new DateOnly(year, 1, 1), PaymentMode.Quarterly);
        using var anonymous = ApiFixture.CreateClient(application);

        var path = await Rotate(owner);
        var secret = path["/api/calendar/feed/".Length..^".ics".Length];
        await anonymous.GetStringAsync(path);
        await anonymous.GetAsync(path + "x");
        var backup = await owner.GetStringAsync("/api/backup");
        var history = await owner.GetStringAsync("/api/audit?limit=200");

        Assert.DoesNotContain(secret, backup);
        Assert.DoesNotContain(secret, history);
        // The levels are appsettings.json's: the framework's request line, which prints the path, is
        // below Warning and so never written.
        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(secret, StringComparison.Ordinal));
    }

    private WebApplicationFactory<Program> At(int year) => At(new FakeClock(new DateTimeOffset(year, 6, 15, 9, 0, 0, TimeSpan.Zero)));

    private WebApplicationFactory<Program> At(FakeClock clock) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));

    // Events carry an owner key in their UID; the tests name events without it.
    private static IcsCalendar Parse(string ics)
    {
        var calendar = IcsCalendar.Load(ics)!;
        foreach (var each in calendar.Events)
        {
            each.Uid = Regex.Replace(each.Uid!, "-[0-9a-f]{8}@taxes-ua$", "@taxes-ua");
        }

        return calendar;
    }

    private static DateOnly DayOf(Ical.Net.CalendarComponents.CalendarEvent each) => DateOnly.FromDateTime(each.DtStart!.Value);

    private static List<(string Uid, DateOnly Date)> Dated(IcsCalendar calendar) =>
        [.. calendar.Events.Select(each => (each.Uid!, DayOf(each))).OrderBy(each => each.Item1, StringComparer.Ordinal)];

    private static (int Year, int Quarter) QuarterOf(string uid)
    {
        var match = Regex.Match(uid, @"-(\d{4})-q(\d)@");
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }

    // The triggers of one event's alarms, read from the raw text so the test sees what a client sees.
    private static string[] AlarmTriggers(string ics, string uid)
    {
        var unfolded = Regex.Replace(ics, "\r\n ", string.Empty);
        var start = Regex.Match(unfolded, "UID:" + Regex.Escape(uid[..^"@taxes-ua".Length]) + "-[0-9a-f]{8}@taxes-ua\r\n").Index;
        var end = unfolded.IndexOf("END:VEVENT", start, StringComparison.Ordinal);
        return [.. Regex.Matches(unfolded[start..end], @"TRIGGER:(\S+)").Select(match => match.Groups[1].Value)];
    }

    private static async Task<string> Rotate(HttpClient owner)
    {
        var response = await owner.PostAsync("/api/calendar/feed/rotate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["path"]!.GetValue<string>();
    }

    private static async Task<string> Subscribe(WebApplicationFactory<Program> application, HttpClient owner)
    {
        using var anonymous = ApiFixture.CreateClient(application);
        var response = await anonymous.GetAsync(await Rotate(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // The 2026 parameters, with weekday holidays on the first half-year's statutory dates, so a
    // holiday shift sits beside the weekend ones.
    private static TaxYearConfigInput Config(int year) => new(
        864_700, 500, 100, 2_200, 19, 40, 10, Holidays(year), 864_700L * 1_167, 1_500, [85, 100], 10);

    private static FopSettingsInput Engine(DateOnly registered) => new(
        Weekend, true, true, registered, TaxesUa.Engine.EsvRegistrationMonthPolicy.FullMonth, false);

    private static DateOnly[] Holidays(int year)
    {
        var bare = new TaxYearConfigInput(864_700, 500, 100, 2_200, 19, 40, 10, [], 864_700L * 1_167, 1_500, [85, 100], 10);
        var settings = Engine(new DateOnly(year, 1, 1));
        var q1 = DeadlineCalendar.ForQuarter(year, 1, bare, settings);
        var q2 = DeadlineCalendar.ForQuarter(year, 2, bare, settings);
        return
        [
            .. new[] { q1.Esv.Statutory, q1.Declaration.Statutory, q2.Esv.Statutory, q2.Declaration.Statutory }
                .Where(date => !Weekend.Contains(date.DayOfWeek))
                .Distinct(),
        ];
    }

    private static async Task SetUp(
        HttpClient owner,
        int year,
        DateOnly registered,
        PaymentMode mode,
        int incomeLimitMinWages = 1_167,
        YearQuarter? backOnGroup3From = null,
        string locale = "uk",
        int[]? years = null,
        bool esvExempt = false)
    {
        foreach (var each in years ?? [year, year + 1])
        {
            var taxYear = new TaxYearConfigRequest(
                864_700, 500, 100, 2_200, 1_500, incomeLimitMinWages, [85, 100], 19, 40, 10, 15, 10, Holidays(each), "a test source");
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{each}", taxYear, Json)).StatusCode);
        }

        var settings = new SettingsRequest(
            registered,
            mode,
            Api.Features.Settings.EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: esvExempt,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            Weekend,
            locale,
            "system",
            "UAH",
            backOnGroup3From);
        var saved = await owner.PutAsJsonAsync("/api/settings", settings, Json);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task PostIncome(HttpClient owner, DateOnly valueDate, long amountKop)
    {
        var request = new TransactionRequest(
            valueDate, amountKop, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", request, Json)).StatusCode);
    }

    private static async Task PostPayment(
        HttpClient owner, DateOnly paidOn, PaymentKind kind, long amountKop, int year, int quarter)
    {
        var request = new PaymentRequest(paidOn, kind, amountKop, year, quarter, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/payments", request, Json)).StatusCode);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Set(DateTimeOffset value) => _now = value;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
