using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// What the app holds for one quarter of a year: the filing mark and the files its download routes serve
/// today.
/// </summary>
internal sealed record QuarterDocuments(int Quarter, DeclarationFiling? Filing, DeclarationFileName[] Files);

/// <summary><c>Annex</c> is the annex file's name, null for any quarter but the year's last group 3 one.</summary>
internal sealed record DeclarationFileName(
    DeclarationType Type, string Name, string? Annex, DateTimeOffset GeneratedAt);

internal static class DeclarationDocuments
{
    /// <summary>The quarters of <paramref name="year"/> that have a filing mark or a servable file, in order.</summary>
    public static async Task<QuarterDocuments[]> ListAsync(
        AppDbContext database, string userId, int year, DateOnly today, CancellationToken cancellationToken)
    {
        var filings = await database.DeclarationFilings.AsNoTracking()
            .Where(row => row.UserId == userId && row.Year == year)
            .ToListAsync(cancellationToken);
        var files = await database.DeclarationFiles.AsNoTracking()
            .Where(row => row.UserId == userId && row.Year == year)
            .Select(row => new { row.Quarter, row.Type, row.FileName, row.AnnexFileName, row.GeneratedAt })
            .ToListAsync(cancellationToken);

        return
        [
            .. Enumerable.Range(1, 4)
                .Select(quarter => new QuarterDocuments(
                    quarter,
                    filings.SingleOrDefault(filing => filing.Quarter == quarter),
                    [
                        .. files
                            .Where(file => file.Quarter == quarter
                                && DeclarationsEndpoints.Servable(file.GeneratedAt, year, quarter, today))
                            .OrderBy(file => file.Type)
                            .Select(file => new DeclarationFileName(
                                file.Type, file.FileName, file.AnnexFileName, file.GeneratedAt)),
                    ]))
                .Where(documents => documents.Filing is not null || documents.Files.Length > 0),
        ];
    }

    /// <summary>
    /// The bytes of a file <see cref="ListAsync"/> listed: the declaration, or its annex when
    /// <paramref name="annex"/> is true. Null when the owner has no such file.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(
        AppDbContext database, string userId, int year, int quarter, DeclarationType type, bool annex,
        CancellationToken cancellationToken)
    {
        var file = await database.DeclarationFiles.AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.UserId == userId && row.Year == year && row.Quarter == quarter && row.Type == type,
                cancellationToken);

        return annex ? file?.AnnexContent : file?.Content;
    }
}
