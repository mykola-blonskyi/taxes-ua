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

                var retention = await LoadAsync(database, user.Id, year, cancellationToken);

                return retention is null
                    ? Problems.TaxYearNotFound(year)
                    : Results.Ok(new YearKeepUntilResponse(year, KeepUntilResponse.Of(retention.KeepUntil)));
            })
            .WithTags("Declarations")
            .RequireAuthorization()
            .Produces<YearKeepUntilResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    /// <summary>
    /// The year's keep-until date and whether its declarations are all marked filed, or null when the owner
    /// has no tax year configured for <paramref name="year"/>. The periods screen, the ZIP's table of contents
    /// and the archive reminder all read it, so they state one date and one idea of a finished year.
    /// </summary>
    internal static async Task<YearRetention?> LoadAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (loaded is null)
        {
            return null;
        }

        var marks = await database.DeclarationFilings.AsNoTracking()
            .Where(filing => filing.UserId == userId && filing.Year == year)
            .ToDictionaryAsync(filing => filing.Quarter, filing => new { filing.FiledOn, filing.CreatedAt }, cancellationToken);
        var suspension = await LimitationSuspensionEndpoints.LoadAsync(database, cancellationToken);

        var viewed = loaded.Viewed;
        var registered = viewed.Settings.FopRegistrationDate;
        int[] quarters =
        [
            .. Enumerable.Range(1, 4).Where(quarter => viewed.Accrual.InGroup3(quarter)
                && (registered is not { } date || DeclarationsEndpoints.QuarterEnd(year, quarter) >= date)),
        ];
        var declarations = quarters
            .Select(quarter => new RetainedDeclaration(
                quarter, marks.TryGetValue(quarter, out var mark) ? mark.FiledOn : null))
            .ToArray();
        var finishedAt = quarters.Length > 0 && quarters.All(marks.ContainsKey)
            ? quarters.Max(quarter => marks[quarter].CreatedAt)
            : (DateTimeOffset?)null;

        return new YearRetention(
            DocumentRetention.ForYear(
                year,
                declarations,
                viewed.Config.ToEngineInput(),
                viewed.Settings.ToEngineInput(),
                suspension.ToEngineInput()),
            finishedAt);
    }
}

/// <summary>
/// <c>KeepUntil</c> is null for a year with no group 3 declaration. <c>FinishedAt</c> is set once every
/// declaration the year should have is marked filed: the moment the last of those marks was first recorded.
/// </summary>
internal sealed record YearRetention(KeepUntil? KeepUntil, DateTimeOffset? FinishedAt);

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
