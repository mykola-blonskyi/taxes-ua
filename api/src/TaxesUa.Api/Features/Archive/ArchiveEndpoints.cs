using Microsoft.AspNetCore.Identity;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Payments;

namespace TaxesUa.Api.Features.Archive;

public static class ArchiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/archive").WithTags("Archive").RequireAuthorization()
            .MapGet("/{year:int}", async (
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

                    var invoices = await InvoiceDocuments.ListAsync(database, user.Id, year, cancellationToken);
                    var quarters = await DeclarationDocuments.ListAsync(
                        database, user.Id, year, time.TodayInKyiv(), cancellationToken);
                    var hasReceipts = await ExportEndpoints.AnyAsync(database, user.Id, year, cancellationToken);
                    var hasPayments = await PaymentRegister.AnyAsync(database, user.Id, year, cancellationToken);

                    return Results.Ok(Build(year, invoices, quarters, hasReceipts, hasPayments));
                })
            .Produces<ArchiveResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static ArchiveResponse Build(
        int year,
        InvoiceDocument[] invoices,
        QuarterDocuments[] quarters,
        bool hasReceipts,
        bool hasPayments) => new(
        year,
        [
            .. invoices.Select(invoice => new ArchiveInvoice(
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
            .. quarters.Select(quarter => new ArchiveQuarter(
                quarter.Quarter,
                quarter.Filing is { } filing ? new ArchiveFiling(filing.FiledOn, filing.Type) : null,
                [.. quarter.Files.SelectMany(file => DeclarationFiles(year, quarter.Quarter, file))])),
        ],
        hasReceipts
            ? [.. ExportEndpoints.Extensions.Select(extension => new ArchiveItem(
                $"statement:{year}:{extension}",
                ExportEndpoints.FileName(year, extension),
                $"/api/export/transactions.{extension}?year={year}"))]
            : [],
        hasPayments
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
