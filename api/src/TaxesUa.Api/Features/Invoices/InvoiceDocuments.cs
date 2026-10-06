using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>
/// One issued or cancelled invoice, as another feature lists its PDF. <c>Currency</c> is the ISO code and
/// <c>FileName</c> the name the PDF route serves it under.
/// </summary>
internal sealed record InvoiceDocument(
    Guid Id,
    string Number,
    InvoiceStatus Status,
    DateOnly IssueDate,
    string Client,
    string Currency,
    long TotalMinor,
    string FileName);

internal static class InvoiceDocuments
{
    /// <summary>
    /// The owner's invoices issued in <paramref name="year"/>: issued and cancelled, never drafts. Oldest
    /// first, the order a binder is read in.
    /// </summary>
    public static async Task<InvoiceDocument[]> ListAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var rows = await database.Invoices.AsNoTracking()
            .Include(invoice => invoice.Client)
            .Where(invoice => invoice.UserId == userId
                && invoice.Status != InvoiceStatus.Draft
                && invoice.IssueDate.Year == year)
            .OrderBy(invoice => invoice.IssueDate)
            .ThenBy(invoice => invoice.NumberSequence)
            .ThenBy(invoice => invoice.Id)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(invoice => new InvoiceDocument(
                invoice.Id,
                invoice.Number ?? string.Empty,
                invoice.Status,
                invoice.IssueDate,
                InvoicesEndpoints.ClientName(invoice),
                invoice.Currency.ToString(),
                invoice.TotalMinor,
                InvoicesEndpoints.FileName(invoice))),
        ];
    }
}
