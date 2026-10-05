using System.Text.Json;
using System.Text.Json.Serialization;
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

    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(2);

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
                TimeProvider time,
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

                var (locale, theme) = (stored.Locale, stored.Theme);
                Apply(stored, request);
                if (stored.Locale != locale)
                {
                    stored.LocaleChosenAt = time.GetUtcNow();
                }

                if (stored.Theme != theme)
                {
                    stored.ThemeChosenAt = time.GetUtcNow();
                }

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

        // The shell's language menu and theme toggle save each choice with the moment it was made, and the
        // newest choice from any device wins (ADR-032). A field is applied only when its time is later than
        // the stored one, so a repeated or late request changes nothing, and the answer is what the server
        // holds afterwards, for the browser to take when it lost.
        settings.MapPut("/appearance", async (
                AppearanceRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                TimeProvider time,
                CancellationToken cancellationToken) =>
            {
                var errors = new FieldErrors();
                if (request.Locale is null && request.Theme is null)
                {
                    errors.Set(Field(nameof(request.Locale)), ProblemCodes.InvalidValue, "Locale or Theme is required.");
                }

                if (request.Locale is not null && !Locales.Contains(request.Locale, StringComparer.Ordinal))
                {
                    errors.Set(
                        Field(nameof(request.Locale)),
                        ProblemCodes.InvalidValue,
                        $"{nameof(request.Locale)} must be one of {string.Join(", ", Locales)}.");
                }

                if (request.Theme is not null && !Themes.Contains(request.Theme, StringComparer.Ordinal))
                {
                    errors.Set(
                        Field(nameof(request.Theme)),
                        ProblemCodes.InvalidValue,
                        $"{nameof(request.Theme)} must be one of {string.Join(", ", Themes)}.");
                }

                if (request.ChosenAt is null)
                {
                    errors.Set(Field(nameof(request.ChosenAt)), ProblemCodes.InvalidValue, "ChosenAt is required.");
                }

                if (errors.OrNull() is { } rejected)
                {
                    return Problems.Validation(rejected);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                // A browser sends the language and the theme as two requests at once, so without the lock both
                // could add the owner's first row, or both read the same time and the older write land last.
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var stored = await database.Settings.FindAsync([user.Id], cancellationToken);
                if (stored is null)
                {
                    stored = new Settings { UserId = user.Id };
                    database.Settings.Add(stored);
                }

                // A device whose clock runs ahead would otherwise win every later choice made elsewhere.
                var now = time.GetUtcNow();
                var chosenAt = request.ChosenAt!.Value.ToUniversalTime();
                if (chosenAt > now + AllowedClockSkew)
                {
                    chosenAt = now;
                }

                if (request.Locale is { } locale && IsNewer(chosenAt, stored.LocaleChosenAt))
                {
                    stored.Locale = locale;
                    stored.LocaleChosenAt = chosenAt;
                }

                if (request.Theme is { } theme && IsNewer(chosenAt, stored.ThemeChosenAt))
                {
                    stored.Theme = theme;
                    stored.ThemeChosenAt = chosenAt;
                }

                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(ToResponse(stored));
            })
            .Produces<SettingsResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static bool IsNewer(DateTimeOffset chosenAt, DateTimeOffset? stored) => stored is null || chosenAt > stored;

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
        // Absent means keep: the shell's menus own both, and a form that loaded before the owner switched
        // must not put the old value back.
        settings.Locale = request.Locale ?? settings.Locale;
        settings.Theme = request.Theme ?? settings.Theme;
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
        settings.BackOnGroup3From,
        settings.LocaleChosenAt,
        settings.ThemeChosenAt);

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
            if (value is not null && !allowed.Contains(value, StringComparer.Ordinal))
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

        if (request.BackOnGroup3From is { } back && (back.Quarter is < 1 or > 4 || back.Year is < Limits.MinYear or > Limits.MaxYear))
        {
            var name = nameof(request.BackOnGroup3From);
            errors.Set(
                Field(name),
                ProblemCodes.InvalidQuarter,
                $"{name} must be a quarter from 1 to 4 of a year from {Limits.MinYear} to {Limits.MaxYear}.");
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
    string? Locale,
    string? Theme,
    string DefaultCurrency,
    YearQuarter? BackOnGroup3From = null)
{
    // What the wire reads. Locale and Theme are optional there (absent keeps the stored value), and a
    // positional parameter can only be optional at the end, so the wire constructor lists them last. The
    // constructor above stays the one callers use.
    [JsonConstructor]
    private SettingsRequest(
        DateOnly? fopRegistrationDate,
        PaymentMode paymentMode,
        EsvRegistrationMonthPolicy esvRegistrationMonthPolicy,
        bool esvExempt,
        bool taxPaymentCountsFromStatutoryDeclarationDate,
        bool shiftTaxPaymentFromWeekend,
        DayOfWeek[] weekendDays,
        string defaultCurrency,
        YearQuarter? backOnGroup3From = null,
        string? locale = null,
        string? theme = null)
        : this(
            fopRegistrationDate,
            paymentMode,
            esvRegistrationMonthPolicy,
            esvExempt,
            taxPaymentCountsFromStatutoryDeclarationDate,
            shiftTaxPaymentFromWeekend,
            weekendDays,
            locale,
            theme,
            defaultCurrency,
            backOnGroup3From)
    {
    }
}

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
    YearQuarter? BackOnGroup3From,
    DateTimeOffset? LocaleChosenAt,
    DateTimeOffset? ThemeChosenAt);

internal sealed record AppearanceRequest(string? Locale = null, string? Theme = null, DateTimeOffset? ChosenAt = null);
