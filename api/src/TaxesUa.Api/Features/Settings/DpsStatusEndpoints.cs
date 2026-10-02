using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Settings;

/// <summary>
/// What the DPS register holds for the FOP: the single-tax group 3 record and the owner's ticks for the
/// other registrations. Kept apart from <c>/api/settings</c> because the FOP form PUTs every settings
/// field, and would wipe these with values it never showed.
/// </summary>
public static class DpsStatusEndpoints
{
    private const int MaxReceiptNumberLength = 64;

    public static IEndpointRouteBuilder MapDpsStatusApi(this IEndpointRouteBuilder routes)
    {
        var status = routes.MapGroup("/settings/dps-status").WithTags("Settings").RequireAuthorization();

        status.MapGet("", async (
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

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                return Results.Ok(await ToResponseAsync(database, settings, cancellationToken));
            })
            .Produces<DpsStatusResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        status.MapPut("", async (
                DpsStatusRequest request,
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

                var settings = await database.Settings.FindAsync([user.Id], cancellationToken);
                var normalized = Normalize(request);
                if (Validate(normalized, settings?.FopRegistrationDate) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                if (settings is null)
                {
                    settings = new Settings { UserId = user.Id };
                    database.Settings.Add(settings);
                }

                Apply(settings, normalized);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(await ToResponseAsync(database, settings, cancellationToken));
            })
            .Produces<DpsStatusResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    internal static DpsStatusRequest Normalize(DpsStatusRequest request) => request with
    {
        Confirmation = request.Confirmation is { } confirmation
            ? confirmation with { ReceiptNumber = confirmation.ReceiptNumber.Trim() }
            : null,
    };

    /// <summary>Validates a request already passed through <see cref="Normalize"/>.</summary>
    internal static Dictionary<string, string[]>? Validate(DpsStatusRequest request, DateOnly? registrationDate)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Group3Since is { } since)
        {
            if (registrationDate is not { } registered)
            {
                errors["group3Since"] = ["group3Since requires fopRegistrationDate in settings."];
            }
            else if (since != registered && !(since > registered && since.Day == 1 && since.Month % 3 == 1))
            {
                errors["group3Since"] =
                    ["group3Since must be the registration date or the first day of a later quarter."];
            }
        }

        if (request.Confirmation is { } confirmation)
        {
            if (confirmation.ReceiptNumber.Length == 0)
            {
                errors["confirmation.receiptNumber"] = ["receiptNumber is required."];
            }
            else if (confirmation.ReceiptNumber.Length > MaxReceiptNumberLength)
            {
                errors["confirmation.receiptNumber"] =
                    [$"receiptNumber must not exceed {MaxReceiptNumberLength} characters."];
            }
            else if (TextRules.HasDisallowedControlChar(confirmation.ReceiptNumber))
            {
                errors["confirmation.receiptNumber"] = ["receiptNumber must not contain control characters."];
            }

            if (registrationDate is not { } registered)
            {
                errors["confirmation.confirmedOn"] = ["confirmation requires fopRegistrationDate in settings."];
            }
            else if (confirmation.ConfirmedOn < registered)
            {
                errors["confirmation.confirmedOn"] = ["confirmedOn must not be before the registration date."];
            }
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static void Apply(Settings settings, DpsStatusRequest request)
    {
        settings.Group3Since = request.Group3Since;
        settings.Group3ConfirmedOn = request.Confirmation?.ConfirmedOn;
        settings.Group3ReceiptNumber = request.Confirmation?.ReceiptNumber;
        settings.DpsFopRegistered = request.FopRegistered;
        settings.DpsEsvRegistered = request.EsvRegistered;
        settings.DpsAccountsRegistered = request.AccountsRegistered;
    }

    /// <summary>
    /// The registration year's parameters, the ones Tax Code 298.1.2's deadline is counted with, as in
    /// the reminder plan. Null without a registration date or without that year's row.
    /// </summary>
    internal static async Task<TaxYearConfigInput?> RegistrationYearConfigAsync(
        AppDbContext database, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.FopRegistrationDate is not { } registered)
        {
            return null;
        }

        var config = await database.TaxYearConfigs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Year == registered.Year, cancellationToken);
        return config?.ToEngineInput();
    }

    private static async Task<DpsStatusResponse> ToResponseAsync(
        AppDbContext database, Settings settings, CancellationToken cancellationToken) => new(
        settings.Group3Since,
        ConfirmationOf(settings),
        settings.DpsFopRegistered,
        settings.DpsEsvRegistered,
        settings.DpsAccountsRegistered,
        settings.FopRegistrationDate,
        settings.ToEngineInput().Group3Start,
        settings.FopRegistrationDate is { } registered
            && await RegistrationYearConfigAsync(database, settings, cancellationToken) is { } config
                ? Group3Application.Deadline(registered, config)
                : null);

    internal static Group3ConfirmationDto? ConfirmationOf(Settings settings) =>
        settings is { Group3ConfirmedOn: { } confirmedOn, Group3ReceiptNumber: { } receiptNumber }
            ? new Group3ConfirmationDto(confirmedOn, receiptNumber)
            : null;
}

/// <summary>The DPS receipt for the group 3 application.</summary>
internal sealed record Group3ConfirmationDto(DateOnly ConfirmedOn, string ReceiptNumber);

/// <summary>
/// <c>Group3Since</c> is the day the DPS register has group 3 from, null for the registration date; a
/// later day is the first day of a quarter (Tax Code 298.1.4).
/// </summary>
internal sealed record DpsStatusRequest(
    DateOnly? Group3Since,
    Group3ConfirmationDto? Confirmation,
    bool FopRegistered,
    bool EsvRegistered,
    bool AccountsRegistered);

/// <summary>
/// The stored status with what follows from it: <c>Group3Start</c> is the first day the app computes
/// group 3 for, and <c>ApplicationDeadline</c> the last day of Tax Code 298.1.2's window, null without
/// a registration date or without the registration year's parameters.
/// </summary>
internal sealed record DpsStatusResponse(
    DateOnly? Group3Since,
    Group3ConfirmationDto? Confirmation,
    bool FopRegistered,
    bool EsvRegistered,
    bool AccountsRegistered,
    DateOnly? FopRegistrationDate,
    DateOnly? Group3Start,
    DateOnly? ApplicationDeadline);
