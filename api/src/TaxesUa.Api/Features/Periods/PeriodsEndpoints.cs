using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;
using FopSettings = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Periods;

public static class PeriodsEndpoints
{
    public static IEndpointRouteBuilder MapPeriodsApi(this IEndpointRouteBuilder routes)
    {
        var periods = routes.MapGroup("/periods").WithTags("Periods").RequireAuthorization();

        periods.MapGet("/{year:int}", async (
                int year,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var config = await database.TaxYearConfigs.FindAsync([year], cancellationToken);
                if (config is null)
                {
                    return Missing(year);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var stored = await database.Settings.FindAsync([user.Id], cancellationToken);
                var settings = stored ?? new FopSettings { UserId = user.Id };

                var configInput = config.ToEngineInput();
                var settingsInput = settings.ToEngineInput();
                var registrationDate = settingsInput.FopRegistrationDate;

                var quarters = new List<QuarterPeriodResponse>();
                for (var quarter = 1; quarter <= 4; quarter++)
                {
                    if (registrationDate is { } registered && QuarterEnd(year, quarter) < registered)
                    {
                        continue;
                    }

                    var deadlines = DeadlineCalendar.ForQuarter(year, quarter, configInput, settingsInput);
                    quarters.Add(new QuarterPeriodResponse(quarter, deadlines));
                }

                return Results.Ok(new PeriodsResponse(year, [.. quarters]));
            })
            .Produces<PeriodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static DateOnly QuarterEnd(int year, int quarter) =>
        new DateOnly(year, 3 * quarter, 1).AddMonths(1).AddDays(-1);

    private static IResult Missing(int year) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No tax year configuration exists for {year}.");
}

internal sealed record PeriodsResponse(int Year, QuarterPeriodResponse[] Quarters);

internal sealed record QuarterPeriodResponse(int Quarter, QuarterDeadlines Deadlines);
