using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
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

                var (declaration, problem) = await LoadAsync(database, user.Id, year, quarter, time, cancellationToken);
                return problem ?? Results.Ok(declaration!.Response);
            })
            .Produces<DeclarationResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        declarations.MapPost("/files", async (
                int year,
                int quarter,
                DeclarationFileRequest request,
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

                var (declaration, problem) = await LoadAsync(database, user.Id, year, quarter, time, cancellationToken);
                if (problem is not null)
                {
                    return problem;
                }

                if (QuarterNotEnded(year, quarter, time.TodayInKyiv()) is { } notEnded)
                {
                    return notEnded;
                }

                if (!declaration!.Response.Readiness.Ready || declaration.Figures is not { } figures)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"The declaration for quarter {quarter} of {year} is not ready, so it has no file.");
                }

                // Ready means no detail is missing, so both rows and every value the header reads exist.
                var invoicing = declaration.Invoicing!;
                var details = declaration.Details!;
                var header = new DeclarationHeader(
                    invoicing.Rnokpp,
                    details.TaxOfficeRegion!.Value,
                    details.TaxOfficeDistrict!.Value,
                    details.TaxOfficeName,
                    invoicing.SellerNameUk,
                    details.Address,
                    details.KvedCodes);
                var errors = DpsXml.Unwritable(header);
                DeclarationXmlFiles? xml = null;
                if (errors.Length == 0)
                {
                    xml = F0103309.Write(figures, header, request.Type, time.TodayInKyiv());
                    errors =
                    [
                        .. F0103309.SchemaErrors(xml.Declaration.Content),
                        .. xml.Annex is { } annex
                            ? F0133109.SchemaErrors(annex.Content).Select(error => $"{annex.FileName} {error}")
                            : [],
                    ];
                }

                if (errors.Length > 0)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = errors },
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "The declaration's data cannot produce files that pass the F0103309 and F0133109 schemas.");
                }

                var file = await database.DeclarationFiles.FindAsync([user.Id, year, quarter, request.Type], cancellationToken);
                if (file is null)
                {
                    file = new DeclarationFile { UserId = user.Id, Year = year, Quarter = quarter, Type = request.Type };
                    database.DeclarationFiles.Add(file);
                }

                file.FileName = xml!.Declaration.FileName;
                file.Content = xml.Declaration.Content;
                file.AnnexFileName = xml.Annex?.FileName;
                file.AnnexContent = xml.Annex?.Content;
                file.GeneratedAt = time.GetUtcNow();
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(new DeclarationFileResponse(file.Type, file.FileName, file.AnnexFileName, file.GeneratedAt));
            })
            .Produces<DeclarationFileResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        declarations.MapGet("/files/{type}", (
                int year,
                int quarter,
                DeclarationType type,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
                DownloadAsync(year, quarter, type, file => (file.FileName, file.Content), users, database, time, http, cancellationToken))
            .Produces<byte[]>(StatusCodes.Status200OK, "application/xml")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        declarations.MapGet("/files/{type}/annex", (
                int year,
                int quarter,
                DeclarationType type,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
                DownloadAsync(year, quarter, type, file => (file.AnnexFileName, file.AnnexContent), users, database, time, http, cancellationToken))
            .Produces<byte[]>(StatusCodes.Status200OK, "application/xml")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

    private static async Task<IResult> DownloadAsync(
        int year,
        int quarter,
        DeclarationType type,
        Func<DeclarationFile, (string? FileName, byte[]? Content)> pick,
        UserManager<ApplicationUser> users,
        AppDbContext database,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(type))
        {
            return Results.NotFound();
        }

        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var file = await database.DeclarationFiles.AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.UserId == user.Id && row.Year == year && row.Quarter == quarter && row.Type == type,
                cancellationToken);
        if (file is null || pick(file) is not (string fileName, byte[] content))
        {
            return Results.NotFound();
        }

        // Checked after the lookup: a year no row can hold is a 404, not an out-of-range date.
        if (QuarterNotEnded(year, quarter, time.TodayInKyiv()) is { } notEnded)
        {
            return notEnded;
        }

        if (IsStale(file.GeneratedAt, year, quarter))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"The file for quarter {quarter} of {year} was generated before the quarter ended, so its figures are incomplete. Generate it again.",
                extensions: new Dictionary<string, object?> { ["reason"] = "GeneratedBeforeQuarterEnded" });
        }

        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        http.Response.Headers.ContentDisposition = disposition.ToString();
        http.Response.Headers.CacheControl = "private, no-store";

        return Results.File(content, "application/xml");
    }

    /// <summary>
    /// The 409 for a quarter whose last day has not passed in Kyiv, or null once it has. <c>reason</c> is
    /// a closed code and <c>availableFrom</c> the first day the file can be built.
    /// </summary>
    private static IResult? QuarterNotEnded(int year, int quarter, DateOnly today)
    {
        if (Declaration.FileAvailable(year, quarter, today))
        {
            return null;
        }

        var from = Declaration.FileAvailableFrom(year, quarter);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: $"Quarter {quarter} of {year} has not ended, so its declaration file can be built from {from:yyyy-MM-dd}.",
            extensions: new Dictionary<string, object?>
            {
                ["reason"] = "QuarterNotEnded",
                ["availableFrom"] = from.ToString("yyyy-MM-dd"),
            });
    }

    /// <summary>
    /// A file generated before its quarter's last day had passed in Kyiv holds incomplete figures (Rule 15),
    /// so it is never listed or served, even after the quarter ends.
    /// </summary>
    private static bool IsStale(DateTimeOffset generatedAt, int year, int quarter) =>
        !Declaration.FileAvailable(year, quarter, generatedAt.KyivDate());

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

    /// <summary>
    /// What GET shows and POST /files writes from, loaded in one place so the two cannot disagree on
    /// the figures or the readiness.
    /// </summary>
    private static async Task<(QuarterDeclaration? Declaration, IResult? Problem)> LoadAsync(
        AppDbContext database,
        string userId,
        int year,
        int quarter,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (Unavailable(loaded, year, quarter) is { } problem)
        {
            return (null, problem);
        }

        var today = time.TodayInKyiv();
        var viewed = loaded!.Viewed;
        var config = viewed.Config.ToEngineInput();
        var settings = viewed.Settings.ToEngineInput();
        var incomeKop = IncomeThrough(loaded, quarter);
        var inGroup3 = viewed.Accrual.InGroup3(quarter);

        var yearStart = new DateOnly(year, 1, 1);
        var quarterEnd = QuarterEnd(year, quarter);
        var receiptsToReview = await database.Transactions.CountAsync(
            row => row.UserId == userId
                && row.ReviewStatus == ReviewStatus.NeedsReview
                && row.ValueDate >= yearStart
                && row.ValueDate <= quarterEnd,
            cancellationToken);
        var pendingCandidates = await PaymentCandidatesEndpoints.CountPendingAsync(
            database, userId, QuarterStart(year, quarter), quarterEnd, cancellationToken);
        var invoicing = await database.InvoicingDetails.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var details = await database.DeclarationDetails.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var ledger = loaded.ViewedIsInLedger
            ? await loaded.PaymentLedgerAsync(database, userId, today, cancellationToken)
            : null;
        var filing = await database.DeclarationFilings.AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.UserId == userId && row.Year == year && row.Quarter == quarter,
                cancellationToken);
        var files = await database.DeclarationFiles.AsNoTracking()
            .Where(row => row.UserId == userId && row.Year == year && row.Quarter == quarter)
            .OrderBy(row => row.Type)
            .Select(row => new DeclarationFileResponse(row.Type, row.FileName, row.AnnexFileName, row.GeneratedAt))
            .ToArrayAsync(cancellationToken);
        files = [.. files.Where(file => !IsStale(file.GeneratedAt, year, quarter))];

        var fileAvailable = Declaration.FileAvailable(year, quarter, today);
        var figures = inGroup3 ? Declaration.ForQuarter(viewed.Accrual, quarter) : null;
        var deadlines = DeadlineCalendar.ForQuarter(year, quarter, config, settings);
        var response = new DeclarationResponse(
            year,
            quarter,
            deadlines.Declaration,
            deadlines.TaxPayment,
            figures is null ? null : ToFigures(figures),
            LimitCrossingResponse.Of(viewed),
            config.SingleTaxRateBp,
            config.ExcessRateBp,
            config.MilitaryLevyRateBp,
            DeclarationReadiness.Evaluate(
                deadlines.Declaration.Due,
                receiptsToReview,
                pendingCandidates,
                viewed.Config.VerifiedAt is not null,
                settings.FopRegistrationDate is not null,
                invoicing,
                details,
                !inGroup3,
                ledger),
            filing is null ? null : ToFiling(filing, incomeKop),
            fileAvailable ? files : [],
            fileAvailable,
            Declaration.FileAvailableFrom(year, quarter));
        return (new QuarterDeclaration(response, figures, invoicing, details), null);
    }

    private sealed record QuarterDeclaration(
        DeclarationResponse Response,
        DeclarationFigures? Figures,
        InvoicingDetails? Invoicing,
        DeclarationDetails? Details);

    private static DateOnly QuarterStart(int year, int quarter) => new(year, 3 * quarter - 2, 1);

    internal static DateOnly QuarterEnd(int year, int quarter) =>
        new DateOnly(year, 3 * quarter, 1).AddMonths(1).AddDays(-1);

    // Line 08: what the filed mark snapshots and the post-filing warning compares against. A quarter
    // outside group 3 has no line 08, so its mark keeps the income from 1 January.
    private static long IncomeThrough(LoadedYears loaded, int quarter)
    {
        var accrual = loaded.Viewed.Accrual;
        return accrual.InGroup3(quarter)
            ? accrual.QuarterOf(quarter).Income.CumulativeIncomeKop
            : accrual.Income.Quarters[quarter - 1].CumulativeIncomeKop;
    }

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
        figures.ExcessIncomeKop,
        figures.TotalIncomeKop,
        figures.ExcessTaxKop,
        figures.SingleTaxKop,
        figures.TotalSingleTaxKop,
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
/// One quarter's declaration (Rule 15). <c>Figures</c> is null for a quarter after the one named by
/// <c>LimitCrossing</c>: group 3 ended there (Rule 4), so the quarter has no group 3 declaration. The
/// rates are the year's, for the lines' labels. The figures of a quarter still running are a preview:
/// <c>FileAvailable</c> is false until <c>FileAvailableFrom</c>, and no file is built or downloaded. <c>Files</c>
/// leaves out a file generated before that date: it is stale and must be generated again.
/// </summary>
internal sealed record DeclarationResponse(
    int Year,
    int Quarter,
    Deadline Filing,
    Deadline Payment,
    DeclarationFiguresResponse? Figures,
    LimitCrossingResponse? LimitCrossing,
    int SingleTaxRateBp,
    int ExcessRateBp,
    int MilitaryLevyRateBp,
    DeclarationReadinessResponse Readiness,
    DeclarationFilingResponse? Filed,
    DeclarationFileResponse[] Files,
    bool FileAvailable,
    DateOnly FileAvailableFrom);

/// <summary>
/// The form's group 3 lines, as <see cref="DeclarationFigures"/> names them: 06, 07, 08, 09, 11, 12,
/// 13, 14.1 and 14, 23, 24, 25, and 21 for the year's last group 3 declaration only.
/// </summary>
internal sealed record DeclarationFiguresResponse(
    long IncomeKop,
    long ExcessIncomeKop,
    long TotalIncomeKop,
    long ExcessTaxKop,
    long SingleTaxKop,
    long TotalSingleTaxKop,
    long PreviousSingleTaxKop,
    long SingleTaxPayableKop,
    long MilitaryLevyKop,
    long PreviousMilitaryLevyKop,
    long MilitaryLevyPayableKop,
    long? EsvKop);

/// <summary>The type decides C_DOC_STAN and the HZ, HZN or HZU mark.</summary>
internal sealed record DeclarationFileRequest(DeclarationType Type);

/// <summary>
/// The last file prepared for the quarter and type; its bytes are at <c>GET files/{type}</c>. The year's
/// last group 3 declaration also has annex 1, named by <c>AnnexFileName</c>, at
/// <c>GET files/{type}/annex</c>; null for every other quarter.
/// </summary>
internal sealed record DeclarationFileResponse(
    DeclarationType Type, string FileName, string? AnnexFileName, DateTimeOffset GeneratedAt);

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
