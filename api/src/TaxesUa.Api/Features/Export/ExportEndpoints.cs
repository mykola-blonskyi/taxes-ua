using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Export;

public static class ExportEndpoints
{
    private sealed record ExportFormat(
        string Extension, string ContentType, Func<IReadOnlyList<Transaction>, byte[]> Write);

    private static readonly ExportFormat[] Formats =
    [
        new("csv", "text/csv; charset=utf-8", TransactionExport.ToCsv),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", TransactionExport.ToXlsx),
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
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    if (year < TransactionsEndpoints.MinYear || year > TransactionsEndpoints.MaxYear)
                    {
                        return Results.ValidationProblem(YearOutOfRange());
                    }

                    var user = await users.GetUserAsync(http.User);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    var rows = await LoadRowsAsync(database, user.Id, year, cancellationToken);

                    return Results.File(
                        format.Write(rows), format.ContentType, $"transactions-{year}.{format.Extension}");
                })
                .Produces<byte[]>(StatusCodes.Status200OK, format.ContentType)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status401Unauthorized);
        }

        return routes;
    }

    // Oldest first, unlike the newest-first list on screen: an accountant reads a ledger forward.
    private static Task<List<Transaction>> LoadRowsAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken) =>
        database.Transactions
            .Include(row => row.Client)
            .Where(row => row.UserId == userId
                && row.ValueDate >= new DateOnly(year, 1, 1)
                && row.ValueDate < new DateOnly(year + 1, 1, 1))
            .OrderBy(row => row.ValueDate)
            .ThenBy(row => row.CreatedAt)
            .ToListAsync(cancellationToken);

    private static Dictionary<string, string[]> YearOutOfRange() => new()
    {
        ["year"] = [$"year must be between {TransactionsEndpoints.MinYear} and {TransactionsEndpoints.MaxYear}."],
    };
}
