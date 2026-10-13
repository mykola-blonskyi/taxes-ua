using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// Rule 19's keep-until date of a year. It lives with the declarations because it is read off their
/// filed marks; the periods screen asks for it beside <c>GET /api/periods/{year}</c>.
/// </summary>
public static class KeepUntilEndpoints
{
    public static IEndpointRouteBuilder MapKeepUntilApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/declarations/{year:int}/keep-until", async (
                int year,
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

                var response = await LoadAsync(database, user.Id, year, cancellationToken);

                return response is null ? Problems.TaxYearNotFound(year) : Results.Ok(response);
            })
            .WithTags("Declarations")
            .RequireAuthorization()
            .Produces<YearKeepUntilResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    /// <summary>
    /// The year's keep-until date as the periods screen shows it, or null when the owner has no tax year
    /// configured for <paramref name="year"/>. Shared with the ZIP's table of contents so both state one date.
    /// </summary>
    internal static async Task<YearKeepUntilResponse?> LoadAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var filedOn = await database.DeclarationFilings.AsNoTracking()
            .Where(filing => filing.UserId == userId && filing.Year == year)
            .ToDictionaryAsync(filing => filing.Quarter, filing => filing.FiledOn, cancellationToken);
        var suspension = await LimitationSuspensionEndpoints.LoadAsync(database, cancellationToken);

        var viewed = loaded.Viewed;
        var registered = viewed.Settings.FopRegistrationDate;
        var declarations = Enumerable.Range(1, 4)
            .Where(quarter => viewed.Accrual.InGroup3(quarter)
                && (registered is not { } date || DeclarationsEndpoints.QuarterEnd(year, quarter) >= date))
            .Select(quarter => new RetainedDeclaration(
                quarter, filedOn.TryGetValue(quarter, out var filed) ? filed : null))
            .ToArray();

        return new YearKeepUntilResponse(
            year,
            KeepUntilResponse.Of(DocumentRetention.ForYear(
                year,
                declarations,
                viewed.Config.ToEngineInput(),
                viewed.Settings.ToEngineInput(),
                suspension.ToEngineInput())));
    }
}

/// <summary><c>KeepUntil</c> is null for a year with no group 3 declaration.</summary>
internal sealed record YearKeepUntilResponse(int Year, KeepUntilResponse? KeepUntil);

/// <summary>
/// <c>Fixed</c>: <c>Date</c> is the last day the year's documents are kept. <c>ExtendedWhileSuspended</c>:
/// the martial-law suspension is open, the count resumes after it ends and runs
/// <c>DaysAfterSuspension</c> more days, and <c>Date</c> is the earliest the result can be.
/// </summary>
internal sealed record KeepUntilResponse(KeepUntilState State, DateOnly Date, int? DaysAfterSuspension)
{
    public static KeepUntilResponse? Of(KeepUntil? until) => until switch
    {
        null => null,
        KeepUntil.On on => new(KeepUntilState.Fixed, on.Date, null),
        KeepUntil.WhileSuspended open => new(KeepUntilState.ExtendedWhileSuspended, open.NotBefore, open.DaysAfterSuspension),
        _ => throw new ArgumentOutOfRangeException(nameof(until), until, "Unmapped keep-until."),
    };
}

internal enum KeepUntilState
{
    Fixed,
    ExtendedWhileSuspended,
}
