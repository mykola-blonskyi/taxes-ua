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
    public async Task Put_rejects_a_repeated_weekend_day()
    {
        var body = Body();
        body["weekendDays"] = new[] { nameof(DayOfWeek.Saturday), nameof(DayOfWeek.Saturday) };

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
        foreach (var name in new[]
                 {
                     nameof(PaymentMode),
                     nameof(EsvRegistrationMonthPolicy),
                     nameof(DayOfWeek),
                 })
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
