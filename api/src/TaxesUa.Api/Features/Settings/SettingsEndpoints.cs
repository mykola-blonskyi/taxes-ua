using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Settings;

public static class SettingsEndpoints
{
    // Strings validated against a set, not enums: Locale would duplicate web/src/i18n/locales.ts,
    // Theme would duplicate next-themes, and a Currency enum belongs to the transactions feature that
    // introduces it, not to this one.
    private static readonly string[] Locales = ["uk", "ru"];

    private static readonly string[] Themes = ["light", "dark", "system"];

    private static readonly string[] Currencies = ["UAH", "USD", "EUR"];

    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder routes)
    {
        var settings = routes.MapGroup("/settings").WithTags("Settings").RequireAuthorization();

        settings.MapGet("", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var stored = await database.Settings.FindAsync([user.Id], cancellationToken);

                return Results.Ok(ToResponse(stored ?? new Settings { UserId = user.Id }));
            })
            .Produces<SettingsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        settings.MapPut("", async (
                SettingsRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var stored = await database.Settings.FindAsync([user.Id], cancellationToken);
                if (stored is null)
                {
                    stored = new Settings { UserId = user.Id };
                    database.Settings.Add(stored);
                }

                Apply(stored, request);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(stored));
            })
            .Produces<SettingsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static void Apply(Settings settings, SettingsRequest request)
    {
        settings.FopRegistrationDate = request.FopRegistrationDate;
        settings.PaymentMode = request.PaymentMode;
        settings.EsvRegistrationMonthPolicy = request.EsvRegistrationMonthPolicy;
        settings.EsvExempt = request.EsvExempt;
        settings.TaxPaymentCountsFromStatutoryDeclarationDate =
            request.TaxPaymentCountsFromStatutoryDeclarationDate;
        settings.ShiftTaxPaymentFromWeekend = request.ShiftTaxPaymentFromWeekend;
        settings.WeekendDays = [.. request.WeekendDays];
        settings.Locale = request.Locale;
        settings.Theme = request.Theme;
        settings.DefaultCurrency = request.DefaultCurrency;
    }

    private static SettingsResponse ToResponse(Settings settings) => new(
        settings.FopRegistrationDate,
        settings.PaymentMode,
        settings.EsvRegistrationMonthPolicy,
        settings.EsvExempt,
        settings.TaxPaymentCountsFromStatutoryDeclarationDate,
        settings.ShiftTaxPaymentFromWeekend,
        settings.WeekendDays,
        settings.Locale,
        settings.Theme,
        settings.DefaultCurrency);

    private static Dictionary<string, string[]>? Validate(SettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var (name, value, allowed) in new[]
                 {
                     (nameof(request.Locale), request.Locale, Locales),
                     (nameof(request.Theme), request.Theme, Themes),
                     (nameof(request.DefaultCurrency), request.DefaultCurrency, Currencies),
                 })
        {
            if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                errors[Field(name)] = [$"{name} must be one of {string.Join(", ", allowed)}."];
            }
        }

        if (request.WeekendDays.Distinct().Count() != request.WeekendDays.Length)
        {
            var name = nameof(request.WeekendDays);
            errors[Field(name)] = [$"{name} must not name a day twice."];
        }

        return errors.Count == 0 ? null : errors;
    }

    // Derived rather than spelled twice, so the key the web reads an error under cannot drift from
    // the member it is about. It is the same policy JsonSerializerDefaults.Web applies.
    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}

internal sealed record SettingsRequest(
    DateOnly? FopRegistrationDate,
    PaymentMode PaymentMode,
    EsvRegistrationMonthPolicy EsvRegistrationMonthPolicy,
    bool EsvExempt,
    bool TaxPaymentCountsFromStatutoryDeclarationDate,
    bool ShiftTaxPaymentFromWeekend,
    DayOfWeek[] WeekendDays,
    string Locale,
    string Theme,
    string DefaultCurrency);

internal sealed record SettingsResponse(
    DateOnly? FopRegistrationDate,
    PaymentMode PaymentMode,
    EsvRegistrationMonthPolicy EsvRegistrationMonthPolicy,
    bool EsvExempt,
    bool TaxPaymentCountsFromStatutoryDeclarationDate,
    bool ShiftTaxPaymentFromWeekend,
    DayOfWeek[] WeekendDays,
    string Locale,
    string Theme,
    string DefaultCurrency);
