using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>
/// The open invoices an imported receipt waiting for review may be paying. The matching is the engine's;
/// this only gathers the receipts and the unpaid invoices and never links anything (Rule 14).
/// </summary>
internal static class InvoiceSuggestions
{
    public static async Task<InvoiceSuggestion[]> ForReviewAsync(
        AppDbContext database,
        string userId,
        DateOnly today,
        Func<Invoice, InvoiceBalance, InvoiceSummary> summarize,
        CancellationToken cancellationToken)
    {
        var receipts = await database.Transactions.AsNoTracking()
            .Where(row => row.UserId == userId
                && row.ReviewStatus == ReviewStatus.NeedsReview
                && row.Kind == TransactionKind.Income
                && row.InvoiceId == null
                && row.BankAccountId != null)
            .Select(row => new { row.Id, row.Currency, row.AmountMinor, row.Counterparty, row.Description })
            .ToListAsync(cancellationToken);
        if (receipts.Count == 0)
        {
            return [];
        }

        var issued = await database.Invoices.AsNoTracking().Include(invoice => invoice.Client)
            .Where(invoice => invoice.UserId == userId && invoice.Status == InvoiceStatus.Issued)
            .ToListAsync(cancellationToken);
        var paying = await InvoicePayments.ReceiptsAsync(database, userId, [.. issued.Select(invoice => invoice.Id)], cancellationToken);
        var open = issued
            .Select(invoice => (Invoice: invoice, Balance: InvoicePayments.Balance(invoice, paying[invoice.Id], today)))
            .Where(row => row.Balance.Standing != InvoiceStanding.Paid)
            .ToDictionary(row => row.Invoice.Id);
        var candidates = open.Values
            .Select(row => new OpenInvoice(
                row.Invoice.Id,
                row.Invoice.Number ?? string.Empty,
                row.Invoice.Snapshot?.Buyer.Name ?? row.Invoice.Client?.Name ?? string.Empty,
                row.Invoice.Currency.ToString(),
                row.Balance.DueMinor ?? 0,
                row.Invoice.DueDate))
            .ToList();

        return [.. receipts
            .Select(receipt => (
                receipt.Id,
                Ids: InvoiceMatcher.Suggest(
                    new ReceiptToMatch(receipt.Currency.ToString(), receipt.AmountMinor, [receipt.Counterparty, receipt.Description]),
                    candidates)))
            .Where(match => match.Ids.Count > 0)
            .Select(match => new InvoiceSuggestion(
                match.Id,
                [.. match.Ids.Select(id => summarize(open[id].Invoice, open[id].Balance))]))];
    }
}

/// <summary>A receipt waiting for review and the open invoices it may be paying, closest due date first.</summary>
internal sealed record InvoiceSuggestion(Guid ReceiptId, InvoiceSummary[] Invoices);
