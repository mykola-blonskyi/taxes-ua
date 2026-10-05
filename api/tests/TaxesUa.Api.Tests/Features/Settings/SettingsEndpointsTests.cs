using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Settings;

// xUnit fixes no order between these tests, so only the first owner is ever written. Every test that
// touches the second owner expects no stored row, which holds whichever order they run in.
public sealed class SettingsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // The web will speak this dialect, generated from the OpenAPI document, so the tests speak it too
    // rather than falling back to the numeric enums a default client would send.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task An_owner_without_a_row_gets_the_defaults_and_nothing_is_stored()
    {
        using var client = await SignIn(ApiFixture.SecondAllowedEmail);

        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);

        AssertDefaults(settings);
        Assert.False(
            await HasStoredSettings(ApiFixture.SecondAllowedEmail),
            "reading the defaults persisted a settings row");
    }

    [Fact]
    public async Task Put_then_get_round_trips_every_field_and_leaves_the_other_owner_alone()
    {
        var desired = new SettingsRequest(
            FopRegistrationDate: new DateOnly(2026, 3, 17),
            PaymentMode: PaymentMode.MonthlyAdvance,
            EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.Prorated,
            EsvExempt: true,
            TaxPaymentCountsFromStatutoryDeclarationDate: false,
            ShiftTaxPaymentFromWeekend: false,
            WeekendDays: [DayOfWeek.Friday, DayOfWeek.Sunday],
            Locale: "ru",
            Theme: "dark",
            DefaultCurrency: "EUR");
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var written = await owner.PutAsJsonAsync("/api/settings", desired, Json);

        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        foreach (var settings in new[]
                 {
                     await written.Content.ReadFromJsonAsync<SettingsResponse>(Json),
                     await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json),
                 })
        {
            Assert.NotNull(settings);
            Assert.Equal(desired.FopRegistrationDate, settings.FopRegistrationDate);
            Assert.Equal(desired.PaymentMode, settings.PaymentMode);
            Assert.Equal(desired.EsvRegistrationMonthPolicy, settings.EsvRegistrationMonthPolicy);
            Assert.Equal(desired.EsvExempt, settings.EsvExempt);
            Assert.Equal(
                desired.TaxPaymentCountsFromStatutoryDeclarationDate,
                settings.TaxPaymentCountsFromStatutoryDeclarationDate);
            Assert.Equal(desired.ShiftTaxPaymentFromWeekend, settings.ShiftTaxPaymentFromWeekend);
            Assert.Equal(desired.WeekendDays, settings.WeekendDays);
            Assert.Equal(desired.Locale, settings.Locale);
            Assert.Equal(desired.Theme, settings.Theme);
            Assert.Equal(desired.DefaultCurrency, settings.DefaultCurrency);
        }

        var raw = await owner.GetStringAsync("/api/settings");
        Assert.Contains("\"paymentMode\":\"MonthlyAdvance\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"weekendDays\":[\"Friday\",\"Sunday\"]", raw, StringComparison.Ordinal);

        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        AssertDefaults(await other.GetFromJsonAsync<SettingsResponse>("/api/settings", Json));
        Assert.False(
            await HasStoredSettings(ApiFixture.SecondAllowedEmail),
            "one owner's write created a row for another");

        var reread = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal(desired.Locale, reread!.Locale);
        Assert.Equal(desired.PaymentMode, reread.PaymentMode);
    }

    [Theory]
    [InlineData("locale", "de")]
    [InlineData("theme", "solarized")]
    [InlineData("defaultCurrency", "GBP")]
    public async Task Put_rejects_a_value_the_interface_does_not_ship(string field, string value)
    {
        var body = Body();
        body[field] = value;

        await AssertRejectedWithoutStoring(body, field);
    }

    [Fact]
    public async Task Put_appearance_changes_only_the_fields_sent()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await ForgetAppearanceTimes();
        var body = Body();
        body["paymentMode"] = nameof(PaymentMode.MonthlyAdvance);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", body, Json)).StatusCode);
        var now = WholeSeconds(DateTimeOffset.UtcNow);

        var language = await Appearance(owner, new { locale = "ru", chosenAt = now.AddSeconds(1) });
        Assert.Equal(("ru", "system", PaymentMode.MonthlyAdvance), (language.Locale, language.Theme, language.PaymentMode));

        var theme = await Appearance(owner, new { theme = "dark", chosenAt = now.AddSeconds(2) });
        Assert.Equal(("ru", "dark", PaymentMode.MonthlyAdvance), (theme.Locale, theme.Theme, theme.PaymentMode));
        Assert.Equal((now.AddSeconds(1), now.AddSeconds(2)), (theme.LocaleChosenAt, theme.ThemeChosenAt));
        var read = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal((theme.Locale, theme.LocaleChosenAt, theme.Theme, theme.ThemeChosenAt), (read!.Locale, read.LocaleChosenAt, read.Theme, read.ThemeChosenAt));
    }

    [Fact]
    public async Task Put_appearance_takes_a_newer_choice_and_ignores_an_older_or_repeated_one()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await ForgetAppearanceTimes();
        var now = WholeSeconds(DateTimeOffset.UtcNow);

        var phone = await Appearance(owner, new { locale = "ru", theme = "dark", chosenAt = now.AddSeconds(3) });
        var latePc = await Appearance(owner, new { locale = "uk", theme = "light", chosenAt = now.AddSeconds(-30) });
        var repeated = await Appearance(owner, new { locale = "uk", chosenAt = now.AddSeconds(3) });

        Assert.Equal(("ru", "dark", now.AddSeconds(3)), (phone.Locale, phone.Theme, phone.LocaleChosenAt));
        Assert.Equal(("ru", "dark", now.AddSeconds(3)), (latePc.Locale, latePc.Theme, latePc.LocaleChosenAt));
        Assert.Equal(("ru", now.AddSeconds(3)), (repeated.Locale, repeated.LocaleChosenAt));

        var newer = await Appearance(owner, new { locale = "uk", chosenAt = now.AddSeconds(4) });
        Assert.Equal(("uk", now.AddSeconds(4), "dark", now.AddSeconds(3)), (newer.Locale, newer.LocaleChosenAt, newer.Theme, newer.ThemeChosenAt));
    }

    [Fact]
    public async Task Put_appearance_holds_a_time_from_the_future_to_the_server_clock()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await ForgetAppearanceTimes();
        var before = DateTimeOffset.UtcNow;

        var ahead = await Appearance(owner, new { theme = "dark", chosenAt = before.AddDays(1) });

        Assert.Equal("dark", ahead.Theme);
        Assert.InRange(ahead.ThemeChosenAt!.Value, before, DateTimeOffset.UtcNow);

        // The clamped choice is not stuck on top: a choice made a moment later, elsewhere, still wins.
        var later = await Appearance(owner, new { theme = "light", chosenAt = DateTimeOffset.UtcNow.AddSeconds(1) });
        Assert.Equal("light", later.Theme);
    }

    [Fact]
    public async Task Put_settings_without_locale_and_theme_keeps_them_and_their_times()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await ForgetAppearanceTimes();
        var chosenAt = WholeSeconds(DateTimeOffset.UtcNow).AddSeconds(5);
        await Appearance(owner, new { locale = "ru", theme = "dark", chosenAt });
        var body = Body();
        body.Remove("locale");
        body.Remove("theme");

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", body, Json)).StatusCode);

        var settings = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal(("ru", "dark", chosenAt, chosenAt), (settings!.Locale, settings.Theme, settings.LocaleChosenAt, settings.ThemeChosenAt));
    }

    [Fact]
    public async Task Put_appearance_sent_at_once_for_an_owner_without_a_row_keeps_the_newest_of_each()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id).SingleAsync();
            await database.Settings.Where(row => row.UserId == userId).ExecuteDeleteAsync();
        }
        var now = WholeSeconds(DateTimeOffset.UtcNow);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => owner.PutAsJsonAsync(
            "/api/settings/appearance",
            index % 2 == 0
                ? new { locale = (string?)(index == 6 ? "ru" : "uk"), theme = (string?)null, chosenAt = now.AddSeconds(index) }
                : new { locale = (string?)null, theme = (string?)(index == 7 ? "dark" : "light"), chosenAt = now.AddSeconds(index) },
            Json)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var settings = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal(("ru", now.AddSeconds(6), "dark", now.AddSeconds(7)), (settings!.Locale, settings.LocaleChosenAt, settings.Theme, settings.ThemeChosenAt));
    }

    [Fact]
    public async Task Put_settings_that_changes_the_locale_counts_as_a_choice_made_now()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await ForgetAppearanceTimes();
        var before = WholeSeconds(DateTimeOffset.UtcNow);
        await Appearance(owner, new { locale = "uk", chosenAt = before.AddSeconds(-1) });
        var body = Body();
        body["locale"] = "ru";

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", body, Json)).StatusCode);

        var settings = await owner.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);
        Assert.Equal("ru", settings!.Locale);
        Assert.InRange(settings.LocaleChosenAt!.Value, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("""{"locale":"de","chosenAt":"2026-10-05T10:00:00Z"}""", "locale")]
    [InlineData("""{"theme":"solarized","chosenAt":"2026-10-05T10:00:00Z"}""", "theme")]
    [InlineData("""{"chosenAt":"2026-10-05T10:00:00Z"}""", "locale")]
    [InlineData("""{"locale":"ru"}""", "chosenAt")]
    public async Task Put_appearance_rejects_a_value_the_interface_does_not_ship_and_stores_nothing(string json, string field)
    {
        using var owner = await SignIn(ApiFixture.SecondAllowedEmail);

        var response = await owner.PutAsync(
            "/api/settings/appearance", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await HasStoredSettings(ApiFixture.SecondAllowedEmail));
    }

    [Fact]
    public async Task Put_appearance_needs_a_signed_in_owner()
    {
        using var client = fixture.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings/appearance", new { locale = "ru", chosenAt = DateTimeOffset.UtcNow }, Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_a_repeated_weekend_day()
    {
        var body = Body();
        body["weekendDays"] = new[] { nameof(DayOfWeek.Saturday), nameof(DayOfWeek.Saturday) };

        await AssertRejectedWithoutStoring(body, "weekendDays");
    }

    // The engine's NextBusinessDay scans forward looking for a day that is neither a weekend day nor
    // a holiday and throws once it exhausts a year, which would turn a periods request into a 500.
    [Fact]
    public async Task Put_rejects_a_weekend_naming_every_day()
    {
        var body = Body();
        body["weekendDays"] = Enum.GetValues<DayOfWeek>().Select(day => day.ToString()).ToArray();

        await AssertRejectedWithoutStoring(body, "weekendDays");
    }

    // The string converter still reads numbers unless allowIntegerValues is off, and the number path
    // checks no enum member, so this is the shape that would store an undefined PaymentMode.
    [Fact]
    public async Task Put_rejects_an_enum_sent_as_a_number()
    {
        var body = Body();
        body["paymentMode"] = 77;

        await AssertRejectedWithoutStoring(body, expectedInBody: null);
    }

    // Enum.TryParse ORs a comma-separated list of member names for any enum: FullMonth is 0 and
    // Prorated is 1, so this string happens to equal the single defined value Prorated.
    // StrictEnumJsonConverter must reject the string outright rather than silently store that value.
    [Fact]
    public async Task Put_rejects_a_comma_joined_esv_policy()
    {
        var body = Body();
        body["esvRegistrationMonthPolicy"] = "FullMonth, Prorated";

        await AssertRejectedWithoutStoring(body, expectedInBody: null);
    }

    [Fact]
    public async Task Put_rejects_a_NUL_in_the_locale()
    {
        var body = Body();
        body["locale"] = "u\u0000k";

        await AssertRejectedWithoutStoring(body, "locale");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Every_route_without_a_session_is_unauthorized(string method)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/settings");
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(Body());
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_enums_as_strings()
    {
        using var client = fixture.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/openapi/v1.json"));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { nameof(PaymentMode), nameof(EsvRegistrationMonthPolicy) })
        {
            var values = schemas.GetProperty(name).GetProperty("enum").EnumerateArray().ToArray();
            Assert.NotEmpty(values);
            Assert.All(values, value => Assert.Equal(JsonValueKind.String, value.ValueKind));
        }

        Assert.Contains(
            nameof(PaymentMode.MonthlyAdvance),
            schemas.GetProperty(nameof(PaymentMode))
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));

        // DayOfWeek is a BCL enum reachable only as SettingsRequest.WeekendDays' element type, so
        // System.Text.Json's schema exporter never gives it its own named component the way it does a
        // project enum: it is described inline where it is used instead.
        var weekendDays = schemas.GetProperty(nameof(SettingsRequest))
            .GetProperty("properties")
            .GetProperty("weekendDays");
        var weekendDayValues = weekendDays.GetProperty("items").GetProperty("enum").EnumerateArray().ToArray();
        Assert.NotEmpty(weekendDayValues);
        Assert.All(weekendDayValues, value => Assert.Equal(JsonValueKind.String, value.ValueKind));
        Assert.Contains(nameof(DayOfWeek.Sunday), weekendDayValues.Select(value => value.GetString()));
    }

    // Every rejected body is sent as the second owner, whose row no test writes, so "still no row" is
    // available as the proof that the rejection stored nothing.
    private async Task AssertRejectedWithoutStoring(
        Dictionary<string, object?> body,
        string? expectedInBody)
    {
        using var client = await SignIn(ApiFixture.SecondAllowedEmail);

        var response = await client.PutAsJsonAsync("/api/settings", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (expectedInBody is not null)
        {
            Assert.Contains(
                expectedInBody,
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.False(
            await HasStoredSettings(ApiFixture.SecondAllowedEmail),
            "a rejected body still created a settings row");
    }

    private static void AssertDefaults(SettingsResponse? settings)
    {
        Assert.NotNull(settings);
        Assert.Null(settings.FopRegistrationDate);
        Assert.Equal(PaymentMode.Quarterly, settings.PaymentMode);
        Assert.Equal(EsvRegistrationMonthPolicy.FullMonth, settings.EsvRegistrationMonthPolicy);
        Assert.False(settings.EsvExempt);
        Assert.True(settings.TaxPaymentCountsFromStatutoryDeclarationDate);
        Assert.True(settings.ShiftTaxPaymentFromWeekend);
        Assert.Equal(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, settings.WeekendDays);
        Assert.Equal("uk", settings.Locale);
        Assert.Equal("system", settings.Theme);
        Assert.Equal("UAH", settings.DefaultCurrency);
    }

    private static async Task<SettingsResponse> Appearance(HttpClient owner, object body)
    {
        var response = await owner.PutAsJsonAsync("/api/settings/appearance", body, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SettingsResponse>(Json))!;
    }

    private static Dictionary<string, object?> Body() => new()
    {
        ["fopRegistrationDate"] = null,
        ["paymentMode"] = nameof(PaymentMode.Quarterly),
        ["esvRegistrationMonthPolicy"] = nameof(EsvRegistrationMonthPolicy.FullMonth),
        ["esvExempt"] = false,
        ["taxPaymentCountsFromStatutoryDeclarationDate"] = true,
        ["shiftTaxPaymentFromWeekend"] = true,
        ["weekendDays"] = new[] { nameof(DayOfWeek.Saturday), nameof(DayOfWeek.Sunday) },
        ["locale"] = "uk",
        ["theme"] = "system",
        ["defaultCurrency"] = "UAH",
    };

    // Postgres keeps microseconds, so a time sent with .NET's ticks would not read back equal.
    private static DateTimeOffset WholeSeconds(DateTimeOffset time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));

    // The tests share one owner, and a later choice always wins, so each starts from times the server
    // does not know rather than from whatever an earlier test stamped.
    private async Task ForgetAppearanceTimes()
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users
            .Where(user => user.Email == ApiFixture.AllowedEmail)
            .Select(user => user.Id)
            .SingleAsync();
        await database.Settings
            .Where(row => row.UserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LocaleChosenAt, (DateTimeOffset?)null).SetProperty(row => row.ThemeChosenAt, (DateTimeOffset?)null));
    }

    private async Task<bool> HasStoredSettings(string email)
    {
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();

        return await database.Settings.AnyAsync(row => row.UserId == userId);
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
