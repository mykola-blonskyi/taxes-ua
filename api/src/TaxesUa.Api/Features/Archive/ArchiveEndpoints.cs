using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Archive;

public static class ArchiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveApi(this IEndpointRouteBuilder routes)
    {
        var archive = routes.MapGroup("/archive").WithTags("Archive").RequireAuthorization();
        archive.MapGet("/{year:int}", async (
                    int year,
                    UserManager<ApplicationUser> users,
                    AppDbContext database,
                    TimeProvider time,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    if (year < Limits.MinYear || year > Limits.MaxYear)
                    {
                        return Problems.Validation(
                            "year",
                            ProblemCodes.YearOutOfRange,
                            $"year must be between {Limits.MinYear} and {Limits.MaxYear}.");
                    }

                    var user = await users.GetUserAsync(http.User);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    var contents = await ArchiveContents.LoadAsync(database, user.Id, year, time, cancellationToken);

                    return Results.Ok(Build(year, contents));
                })
            .Produces<ArchiveResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        archive.MapGet("/{year:int}/zip", async (
                    int year,
                    UserManager<ApplicationUser> users,
                    AppDbContext database,
                    TimeProvider time,
                    HttpContext http,
                    ILogger<ArchiveNotice> logger,
                    CancellationToken cancellationToken) =>
                {
                    if (year < Limits.MinYear || year > Limits.MaxYear)
                    {
                        return Problems.Validation(
                            "year",
                            ProblemCodes.YearOutOfRange,
                            $"year must be between {Limits.MinYear} and {Limits.MaxYear}.");
                    }

                    var user = await users.GetUserAsync(http.User);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    // The moment the contents are read: a mark made while the ZIP streams is newer than this, so
                    // the download does not answer it.
                    var readAt = time.GetUtcNow();
                    var contents = await ArchiveContents.LoadAsync(database, user.Id, year, time, cancellationToken);
                    var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                    var today = time.TodayInKyiv();
                    var keepUntil = KeepUntilResponse.Of(
                        (await KeepUntilEndpoints.LoadAsync(database, user.Id, year, cancellationToken))?.KeepUntil);
                    var toc = YearZip.TableOfContents(year, contents, settings.Locale, today, keepUntil);
                    http.Response.Headers.CacheControl = "private, no-store";

                    return Results.Stream(
                        async stream =>
                        {
                            await YearZip.WriteAsync(stream, database, user.Id, year, contents, toc, time, cancellationToken);
                            try
                            {
                                await ArchiveNotice.RecordAsync(
                                    database, user.Id, year, ArchiveNoticeOutcome.Downloaded, readAt, cancellationToken);
                            }
                            catch (Exception exception)
                            {
                                // The ZIP is already delivered; a missed record only leaves the notice up.
                                logger.LogWarning(exception, "The download of the {Year} archive was not recorded.", year);
                            }
                        },
                        "application/zip",
                        YearZip.FileName(year, today));
                })
            .Produces<byte[]>(StatusCodes.Status200OK, "application/zip")
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        archive.MapPost("/{year:int}/notice/dismiss", async (
                    int year,
                    UserManager<ApplicationUser> users,
                    AppDbContext database,
                    TimeProvider time,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    if (year < Limits.MinYear || year > Limits.MaxYear)
                    {
                        return Problems.Validation(
                            "year",
                            ProblemCodes.YearOutOfRange,
                            $"year must be between {Limits.MinYear} and {Limits.MaxYear}.");
                    }

                    var user = await users.GetUserAsync(http.User);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    await ArchiveNotice.RecordAsync(
                        database, user.Id, year, ArchiveNoticeOutcome.Dismissed, time.GetUtcNow(), cancellationToken);

                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static ArchiveResponse Build(int year, ArchiveContents contents) => new(
        year,
        [
            .. contents.Invoices.Select(invoice => new ArchiveInvoice(
                $"invoice:{invoice.Id}",
                invoice.FileName,
                $"/api/invoices/{invoice.Id}/pdf",
                invoice.Number,
                invoice.Status,
                invoice.IssueDate,
                invoice.Client,
                invoice.Currency,
                invoice.TotalMinor)),
        ],
        [
            .. contents.Quarters.Select(quarter => new ArchiveQuarter(
                quarter.Quarter,
                quarter.Filing is { } filing ? new ArchiveFiling(filing.FiledOn, filing.Type) : null,
                [.. quarter.Files.SelectMany(file => DeclarationFiles(year, quarter.Quarter, file))])),
        ],
        contents.HasReceipts
            ? [.. ExportEndpoints.Extensions.Select(extension => new ArchiveItem(
                $"statement:{year}:{extension}",
                ExportEndpoints.FileName(year, extension),
                $"/api/export/transactions.{extension}?year={year}"))]
            : [],
        contents.HasPayments
            ? [new ArchiveItem(
                $"payments:{year}:csv",
                PaymentRegister.FileName(year),
                $"/api/payments/register.csv?year={year}")]
            : []);

    private static IEnumerable<ArchiveDeclarationFile> DeclarationFiles(int year, int quarter, DeclarationFileName file)
    {
        var route = $"/api/declarations/{year}/{quarter}/files/{file.Type}";
        var key = $"{year}-{quarter}-{file.Type}";
        yield return new ArchiveDeclarationFile($"declaration:{key}", file.Type, false, file.Name, route, file.GeneratedAt);
        if (file.Annex is { } annex)
        {
            yield return new ArchiveDeclarationFile($"annex:{key}", file.Type, true, annex, $"{route}/annex", file.GeneratedAt);
        }
    }
}
