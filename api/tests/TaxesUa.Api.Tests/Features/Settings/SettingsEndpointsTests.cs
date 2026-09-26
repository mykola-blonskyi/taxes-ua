using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Settings;

public sealed class SettingsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // The web will speak this dialect, generated from the OpenAPI document, so the tests speak it too
    // rather than falling back to the numeric enums a default client would send.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Only this test reads the second owner's settings, and nothing writes them, so the absence of a
    // row is also the proof that one owner's PUT stays out of another's GET.
    [Fact]
    public async Task An_owner_without_a_row_gets_the_defaults_and_nothing_is_stored()
    {
        using var client = await SignIn(ApiFixture.SecondAllowedEmail);

        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json);

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

        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await database.Users
            .Where(user => user.Email == ApiFixture.SecondAllowedEmail)
            .Select(user => user.Id)
            .SingleAsync();
        Assert.False(
            await database.Settings.AnyAsync(row => row.UserId == userId),
            "reading the defaults persisted a settings row");
    }

    [Fact]
    public async Task Put_then_get_round_trips_every_field()
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
        using var client = await SignIn(ApiFixture.AllowedEmail);

        var written = await client.PutAsJsonAsync("/api/settings", desired, Json);

        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        foreach (var settings in new[]
                 {
                     await written.Content.ReadFromJsonAsync<SettingsResponse>(Json),
                     await client.GetFromJsonAsync<SettingsResponse>("/api/settings", Json),
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

        var raw = await client.GetStringAsync("/api/settings");
        Assert.Contains("\"paymentMode\":\"MonthlyAdvance\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"weekendDays\":[\"Friday\",\"Sunday\"]", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Put_rejects_a_locale_the_interface_does_not_ship()
    {
        using var client = await SignIn(ApiFixture.AllowedEmail);
        var desired = new SettingsRequest(
            FopRegistrationDate: null,
            PaymentMode: PaymentMode.Quarterly,
            EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
            EsvExempt: false,
            TaxPaymentCountsFromStatutoryDeclarationDate: true,
            ShiftTaxPaymentFromWeekend: true,
            WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
            Locale: "de",
            Theme: "dark",
            DefaultCurrency: "EUR");

        var response = await client.PutAsJsonAsync("/api/settings", desired, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            nameof(SettingsRequest.Locale),
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
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
            request.Content = JsonContent.Create(
                new SettingsRequest(
                    FopRegistrationDate: null,
                    PaymentMode: PaymentMode.Quarterly,
                    EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
                    EsvExempt: false,
                    TaxPaymentCountsFromStatutoryDeclarationDate: true,
                    ShiftTaxPaymentFromWeekend: true,
                    WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
                    Locale: "uk",
                    Theme: "system",
                    DefaultCurrency: "UAH"),
                options: Json);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Guards the generated TypeScript: a numeric enum reaches the web as a magic number instead of a
    // string union.
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
