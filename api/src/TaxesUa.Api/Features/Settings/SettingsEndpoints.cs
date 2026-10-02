using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

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
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var stored = await database.Settings.FindAsync([user.Id], cancellationToken);
                if (stored?.Group3ConfirmedOn is { } confirmedOn
                    && !(request.FopRegistrationDate is { } registered && registered <= confirmedOn))
                {
                    return Problems.Validation(
                        "fopRegistrationDate",
                        ProblemCodes.RegistrationDateAfterGroup3Receipt,
                        "fopRegistrationDate must not be after the group 3 confirmation date.");
                }

                if (stored is null)
                {
                    stored = new Settings { UserId = user.Id };
                    database.Settings.Add(stored);
                }

                Apply(stored, request);
                // Only a quarter start after the registration date stays a start of its own; anything else
                // was "from registration", which null says without going stale when the date moves (Rule 8).
                if (stored.Group3Since is { } since
                    && !(stored.FopRegistrationDate is { } registration
                        && DpsStatusEndpoints.IsQuarterStartAfter(since, registration)))
                {
                    stored.Group3Since = null;
                }

                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(stored));
            })
            .Produces<SettingsResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    // The absent row answers with the defaults, as GET /api/settings does, so an owner who never saved
    // settings is treated as having no registration date rather than as an error. Shared by every
    // feature that only reads settings; TransactionsEndpoints and PaymentsEndpoints call this instead
    // of each keeping their own copy.
    internal static async Task<Settings> LoadOrDefaultAsync(
        AppDbContext database, string userId, CancellationToken cancellationToken) =>
        await database.Settings.FindAsync([userId], cancellationToken) ?? new Settings { UserId = userId };

    internal static void Apply(Settings settings, SettingsRequest request)
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
        settings.BackOnGroup3FromYear = request.BackOnGroup3From?.Year;
        settings.BackOnGroup3FromQuarter = request.BackOnGroup3From?.Quarter;
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
        settings.DefaultCurrency,
        settings.BackOnGroup3From);

    internal static FieldErrors? Validate(SettingsRequest request)
    {
        var errors = new FieldErrors();

        foreach (var (name, value, allowed) in new[]
                 {
                     (nameof(request.Locale), request.Locale, Locales),
                     (nameof(request.Theme), request.Theme, Themes),
                     (nameof(request.DefaultCurrency), request.DefaultCurrency, Currencies),
                 })
        {
            if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                errors.Set(
                    Field(name),
                    ProblemCodes.InvalidValue,
                    $"{name} must be one of {string.Join(", ", allowed)}.");
            }
        }

        if (request.WeekendDays.Distinct().Count() != request.WeekendDays.Length)
        {
            var name = nameof(request.WeekendDays);
            errors.Set(Field(name), ProblemCodes.DuplicateValue, $"{name} must not name a day twice.");
        }
        else if (request.WeekendDays.Length == Enum.GetValues<DayOfWeek>().Length)
        {
            var name = nameof(request.WeekendDays);
            errors.Set(Field(name), ProblemCodes.WeekendAllDays, $"{name} must leave at least one working day.");
        }

        if (request.BackOnGroup3From is { } back && (back.Quarter is < 1 or > 4 || back.Year is < TransactionsEndpoints.MinYear or > TransactionsEndpoints.MaxYear))
        {
            var name = nameof(request.BackOnGroup3From);
            errors.Set(
                Field(name),
                ProblemCodes.InvalidQuarter,
                $"{name} must be a quarter from 1 to 4 of a year from {TransactionsEndpoints.MinYear} to {TransactionsEndpoints.MaxYear}.");
        }

        return errors.OrNull();
    }

    // Derived rather than spelled a second time, so the key the web reads an error under cannot drift
    // from the member it is about. CamelCase is the policy JsonSerializerDefaults.Web applies to the
    // same member.
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
    string DefaultCurrency,
    YearQuarter? BackOnGroup3From = null);

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
    string DefaultCurrency,
    YearQuarter? BackOnGroup3From);
