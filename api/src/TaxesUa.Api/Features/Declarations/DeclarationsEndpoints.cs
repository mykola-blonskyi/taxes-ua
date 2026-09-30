using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Declarations;

public static class DeclarationsEndpoints
{
    public static IEndpointRouteBuilder MapDeclarationsApi(this IEndpointRouteBuilder routes)
    {
        var declarations = routes.MapGroup("/declarations/{year:int}/{quarter:int:range(1,4)}")
            .WithTags("Declarations")
            .RequireAuthorization();

        declarations.MapGet("", async (
                int year,
                int quarter,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var loaded = await YearAccruals.LoadAsync(database, user.Id, year, cancellationToken);
                if (Unavailable(loaded, year, quarter) is { } problem)
                {
                    return problem;
                }

                var viewed = loaded!.Viewed;
                var config = viewed.Config.ToEngineInput();
                var settings = viewed.Settings.ToEngineInput();
                var incomeKop = IncomeThrough(loaded, quarter);
                var incomeOverLimit = LimitMonitor.Evaluate(Math.Max(incomeKop, 0), config).ExcessKop > 0;

                var yearStart = new DateOnly(year, 1, 1);
                var quarterEnd = QuarterEnd(year, quarter);
                var receiptsToReview = await database.Transactions.CountAsync(
                    row => row.UserId == user.Id
                        && row.ReviewStatus == ReviewStatus.NeedsReview
                        && row.ValueDate >= yearStart
                        && row.ValueDate <= quarterEnd,
                    cancellationToken);
                var pendingCandidates = await PaymentCandidatesEndpoints.CountPendingAsync(
                    database, user.Id, QuarterStart(year, quarter), quarterEnd, cancellationToken);
                var invoicing = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                var details = await database.DeclarationDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                var ledger = loaded.ViewedIsInLedger
                    ? await loaded.PaymentLedgerAsync(database, user.Id, time.TodayInKyiv(), cancellationToken)
                    : null;
                var filing = await database.DeclarationFilings.AsNoTracking()
                    .FirstOrDefaultAsync(
                        row => row.UserId == user.Id && row.Year == year && row.Quarter == quarter,
                        cancellationToken);

                var deadlines = DeadlineCalendar.ForQuarter(year, quarter, config, settings);
                return Results.Ok(new DeclarationResponse(
                    year,
                    quarter,
                    deadlines.Declaration,
                    deadlines.TaxPayment,
                    incomeOverLimit ? null : ToFigures(Declaration.ForQuarter(viewed.Accrual, quarter)),
                    config.SingleTaxRateBp,
                    config.MilitaryLevyRateBp,
                    DeclarationReadiness.Evaluate(
                        deadlines.Declaration.Due,
                        receiptsToReview,
                        pendingCandidates,
                        viewed.Config.VerifiedAt is not null,
                        settings.FopRegistrationDate is not null,
                        invoicing,
                        details,
                        incomeOverLimit,
                        ledger),
                    filing is null ? null : ToFiling(filing, incomeKop)));
            })
            .Produces<DeclarationResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        declarations.MapPut("/filing", async (
                int year,
                int quarter,
                DeclarationFilingRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var loaded = await YearAccruals.LoadAsync(database, user.Id, year, cancellationToken);
                if (Unavailable(loaded, year, quarter) is { } problem)
                {
                    return problem;
                }

                if (ValidateFiling(year, quarter, request.FiledOn, time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var now = time.GetUtcNow();
                var filing = await database.DeclarationFilings.FindAsync([user.Id, year, quarter], cancellationToken);
                if (filing is null)
                {
                    filing = new DeclarationFiling { UserId = user.Id, Year = year, Quarter = quarter, CreatedAt = now };
                    database.DeclarationFilings.Add(filing);
                }

                var incomeKop = IncomeThrough(loaded!, quarter);
                filing.FiledOn = request.FiledOn;
                filing.Type = request.Type;
                filing.FiledIncomeKop = incomeKop;
                filing.UpdatedAt = now;
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToFiling(filing, incomeKop));
            })
            .Produces<DeclarationFilingResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        declarations.MapDelete("/filing", async (
                int year,
                int quarter,
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

                var filing = await database.DeclarationFilings.FindAsync([user.Id, year, quarter], cancellationToken);
                if (filing is not null)
                {
                    database.DeclarationFilings.Remove(filing);
                    await database.SaveChangesAsync(cancellationToken);
                }

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    /// <summary>The rules a filed mark meets that need no stored row, shared with the restore.</summary>
    internal static Dictionary<string, string[]>? ValidateFiling(int year, int quarter, DateOnly filedOn, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (year is < 1 or > 9998)
        {
            errors["year"] = ["year must be 1 to 9998."];
        }
        else if (quarter is < 1 or > 4)
        {
            errors["quarter"] = ["quarter must be 1 to 4."];
        }
        else if (filedOn <= QuarterEnd(year, quarter))
        {
            errors["filedOn"] = [$"filedOn must be after the quarter's end, {QuarterEnd(year, quarter):yyyy-MM-dd}."];
        }
        else if (filedOn > today)
        {
            errors["filedOn"] = ["filedOn must not be later than today."];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static DateOnly QuarterStart(int year, int quarter) => new(year, 3 * quarter - 2, 1);

    internal static DateOnly QuarterEnd(int year, int quarter) =>
        new DateOnly(year, 3 * quarter, 1).AddMonths(1).AddDays(-1);

    // Line 08: what the filed mark snapshots and the post-filing warning compares against.
    private static long IncomeThrough(LoadedYears loaded, int quarter) =>
        loaded.Viewed.Accrual.Quarters[quarter - 1].Income.CumulativeIncomeKop;

    private static IResult? Unavailable(LoadedYears? loaded, int year, int quarter)
    {
        if (loaded is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"No tax year configuration exists for {year}.");
        }

        return loaded.Viewed.Settings.FopRegistrationDate is { } registered && QuarterEnd(year, quarter) < registered
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Quarter {quarter} of {year} ends before the FOP registration date, so it has no declaration.")
            : null;
    }

    private static DeclarationFiguresResponse ToFigures(DeclarationFigures figures) => new(
        figures.IncomeKop,
        figures.SingleTaxKop,
        figures.PreviousSingleTaxKop,
        figures.SingleTaxPayableKop,
        figures.MilitaryLevyKop,
        figures.PreviousMilitaryLevyKop,
        figures.MilitaryLevyPayableKop,
        figures.EsvKop);

    private static DeclarationFilingResponse ToFiling(DeclarationFiling filing, long incomeKop) => new(
        filing.FiledOn,
        filing.Type,
        filing.FiledIncomeKop,
        filing.FiledIncomeKop != incomeKop);
}

/// <summary>
/// One quarter's declaration (Rule 15). <c>Figures</c> is null when the income through the quarter
/// is over the year's limit, because the 15% lines are not filled yet (#118). The rates are the
/// year's, for the lines' labels.
/// </summary>
internal sealed record DeclarationResponse(
    int Year,
    int Quarter,
    Deadline Filing,
    Deadline Payment,
    DeclarationFiguresResponse? Figures,
    int SingleTaxRateBp,
    int MilitaryLevyRateBp,
    DeclarationReadinessResponse Readiness,
    DeclarationFilingResponse? Filed);

/// <summary>
/// The form's group 3 lines, as <see cref="DeclarationFigures"/> names them: 06 and 08, 11 and 12,
/// 13, 14.1 and 14, 23, 24, 25, and 21 for the annual declaration only.
/// </summary>
internal sealed record DeclarationFiguresResponse(
    long IncomeKop,
    long SingleTaxKop,
    long PreviousSingleTaxKop,
    long SingleTaxPayableKop,
    long MilitaryLevyKop,
    long PreviousMilitaryLevyKop,
    long MilitaryLevyPayableKop,
    long? EsvKop);

internal sealed record DeclarationFilingRequest(DateOnly FiledOn, DeclarationType Type);

/// <summary>
/// <c>ChangedSinceFiling</c> is true when line 08 now differs from <c>FiledIncomeKop</c>, the figure
/// it had when marked: the filed declaration may need a clarifying one. Marking again clears it.
/// </summary>
internal sealed record DeclarationFilingResponse(
    DateOnly FiledOn,
    DeclarationType Type,
    long FiledIncomeKop,
    bool ChangedSinceFiling);
