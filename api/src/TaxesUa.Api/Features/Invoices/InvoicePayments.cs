using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>
/// Which receipts pay an invoice and what that makes it (Rule 14). Paid and overdue are read off the
/// links every time, never stored, so an unlink, a refund or a deleted receipt reopens the invoice
/// without anything to keep in step.
/// </summary>
internal static class InvoicePayments
{
    /// <summary>The receipts linked to each of <paramref name="invoiceIds"/>, oldest first.</summary>
    public static async Task<ILookup<Guid, LinkedReceipt>> ReceiptsAsync(
        AppDbContext database, string userId, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        var rows = await database.Transactions.AsNoTracking()
            .Where(row => row.UserId == userId && row.InvoiceId != null && invoiceIds.Contains(row.InvoiceId.Value))
            .OrderBy(row => row.ValueDate)
            .ThenBy(row => row.CreatedAt)
            .Select(row => new
            {
                InvoiceId = row.InvoiceId!.Value,
                Receipt = new LinkedReceipt(
                    row.Id,
                    row.ValueDate,
                    row.AmountMinor,
                    database.Transactions
                        .Where(refund => refund.RefundsTransactionId == row.Id)
                        .Sum(refund => refund.AmountMinor),
                    row.Client == null ? null : row.Client.Name),
            })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(row => row.InvoiceId, row => row.Receipt);
    }

    public static long PaidMinor(IEnumerable<LinkedReceipt> receipts) => receipts.Sum(receipt => receipt.PaidMinor);

    public static InvoiceBalance Balance(Invoice invoice, IEnumerable<LinkedReceipt> receipts, DateOnly today)
    {
        var paid = PaidMinor(receipts);

        return invoice.Status switch
        {
            InvoiceStatus.Draft => new(InvoiceStanding.Draft, paid, null),
            InvoiceStatus.Cancelled => new(InvoiceStanding.Cancelled, paid, null),
            _ when paid >= invoice.TotalMinor => new(InvoiceStanding.Paid, paid, 0),
            _ => new(
                today > invoice.DueDate ? InvoiceStanding.Overdue : InvoiceStanding.Issued,
                paid,
                invoice.TotalMinor - paid),
        };
    }

    public static async Task<int> CountOverdueAsync(
        AppDbContext database, string userId, DateOnly today, CancellationToken cancellationToken)
    {
        var pastDue = await database.Invoices.AsNoTracking()
            .Where(invoice => invoice.UserId == userId
                && invoice.Status == InvoiceStatus.Issued
                && invoice.DueDate < today)
            .Select(invoice => new { invoice.Id, invoice.TotalMinor })
            .ToListAsync(cancellationToken);
        var receipts = await ReceiptsAsync(database, userId, [.. pastDue.Select(invoice => invoice.Id)], cancellationToken);

        return pastDue.Count(invoice => PaidMinor(receipts[invoice.Id]) < invoice.TotalMinor);
    }

    /// <summary>
    /// Why <paramref name="receipt"/> cannot pay <paramref name="invoice"/>, or null when it can. Both
    /// already belong to the owner; a receipt already paying this invoice is the caller's no-op.
    /// </summary>
    public static Issue? LinkConflict(Invoice invoice, Transaction receipt, long paidMinor) => (invoice, receipt) switch
    {
        (_, { Kind: not TransactionKind.Income }) =>
            new Issue(ProblemCodes.OnlyReceiptsPay, "Only a receipt (Income) can pay an invoice."),
        (_, { InvoiceId: not null }) =>
            new Issue(ProblemCodes.ReceiptAlreadyLinked, "The receipt already pays another invoice. Unlink it there first."),
        ({ Status: InvoiceStatus.Draft }, _) =>
            new Issue(ProblemCodes.InvoiceIsDraft, "A draft cannot be paid. Issue it first."),
        ({ Status: InvoiceStatus.Cancelled }, _) =>
            new Issue(ProblemCodes.InvoiceCancelled, $"Invoice {invoice.Number} is cancelled and cannot be paid."),
        _ when receipt.Currency != invoice.Currency =>
            new Issue(
                ProblemCodes.CurrencyMismatch,
                $"Invoice {invoice.Number} is in {invoice.Currency}; a {receipt.Currency} receipt cannot pay it."),
        _ when paidMinor >= invoice.TotalMinor =>
            new Issue(ProblemCodes.InvoiceAlreadyPaid, $"Invoice {invoice.Number} is already paid."),
        _ => null,
    };
}

/// <summary>
/// A receipt paying an invoice. <c>PaidMinor</c> is what it still pays after the refunds linked to it,
/// so a receipt refunded in full pays nothing.
/// </summary>
internal sealed record LinkedReceipt(Guid Id, DateOnly ValueDate, long AmountMinor, long RefundedMinor, string? ClientName)
{
    public long PaidMinor => AmountMinor - RefundedMinor;
}

/// <summary>
/// The invoice as the owner reads it. <c>DueMinor</c>, the amount still due, is null on a draft and on
/// a cancelled invoice, which nobody pays.
/// </summary>
internal sealed record InvoiceBalance(InvoiceStanding Standing, long PaidMinor, long? DueMinor);

/// <summary>The status the owner sees: the stored one, with an issued invoice read as paid or overdue.</summary>
internal enum InvoiceStanding
{
    Draft,
    Issued,
    Overdue,
    Paid,
    Cancelled,
}
