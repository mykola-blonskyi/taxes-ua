using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Export;

public static class ExportEndpoints
{
    private sealed record ExportFormat(
        string Extension, string ContentType, Func<ExportedYear, byte[]> Write);

    private static readonly ExportFormat[] Formats =
    [
        new("csv", "text/csv; charset=utf-8", year => TransactionExport.ToCsv(year.Rows)),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", year => TransactionExport.ToXlsx(year.Rows)),
        new("pdf", "application/pdf", TransactionPdf.ToPdf),
    ];

    public static IEndpointRouteBuilder MapExportApi(this IEndpointRouteBuilder routes)
    {
        var export = routes.MapGroup("/export")
            .WithTags("Export")
            .RequireAuthorization();

        foreach (var format in Formats)
        {
            export.MapGet($"/transactions.{format.Extension}", async (
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

                    var rows = await LoadRowsAsync(database, user.Id, year, cancellationToken);
                    var settings = await database.Settings.FindAsync([user.Id], cancellationToken)
                        ?? new SettingsEntity { UserId = user.Id };
                    var totalIncomeKop = IncomeLedger.ForYear(
                        year, rows.Select(row => row.ToEngineInput()).ToList(), settings.ToEngineInput()).TotalIncomeKop;
                    var exported = new ExportedYear(year, rows, totalIncomeKop, time.NowInKyiv());

                    return Results.File(
                        format.Write(exported), format.ContentType, FileName(year, format.Extension));
                })
                .Produces<byte[]>(StatusCodes.Status200OK, format.ContentType)
                .ProducesFieldProblem()
                .Produces(StatusCodes.Status401Unauthorized);
        }

        return routes;
    }

    /// <summary>The statement's formats, by file extension.</summary>
    internal static IEnumerable<string> Extensions => Formats.Select(format => format.Extension);

    internal static string FileName(int year, string extension) => $"transactions-{year}.{extension}";

    /// <summary>Whether the year has any row for the statement to list.</summary>
    internal static Task<bool> AnyAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken) =>
        RowsOfYear(database, userId, year).AnyAsync(cancellationToken);

    // Oldest first, unlike the newest-first list on screen: an accountant reads a ledger forward.
    private static Task<List<Transaction>> LoadRowsAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken) =>
        RowsOfYear(database, userId, year)
            .Include(row => row.Client)
            .Include(row => row.RefundsTransaction)
            .OrderBy(row => row.ValueDate)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

    private static IQueryable<Transaction> RowsOfYear(AppDbContext database, string userId, int year) =>
        database.Transactions.Where(row => row.UserId == userId
            && row.ValueDate >= new DateOnly(year, 1, 1)
            && row.ValueDate < new DateOnly(year + 1, 1, 1));
}
