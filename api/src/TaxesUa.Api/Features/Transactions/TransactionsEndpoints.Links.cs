using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Transactions;

public static partial class TransactionsEndpoints
{
    // Validation already loaded the receipt into the context, so this is a lookup, not a query.
    internal static async Task<Transaction?> FindReceiptAsync(
        AppDbContext database, TransactionRequest request, CancellationToken cancellationToken) =>
        request.RefundsTransactionId is { } receiptId
            ? await database.Transactions.FindAsync([receiptId], cancellationToken)
            : null;

    // Rule 8 follows a refund to its receipt, so a link has to point at a receipt that can carry it:
    // the owner's own Income row in the refund's currency, not over-refunded (compared in that
    // currency's minor units), and not turned into something else later.
    internal static async Task<FieldErrors?> ValidateLinksAsync(
        AppDbContext database,
        string userId,
        Transaction? row,
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var editedId = row?.Id;

        if (request.RefundsTransactionId is { } receiptId)
        {
            // The edited row is still Income in the database, so without the id check it could link
            // to itself on the way to becoming a refund.
            var receipt = await database.Transactions.FirstOrDefaultAsync(
                t => t.Id == receiptId
                    && t.Id != editedId
                    && t.UserId == userId
                    && t.Kind == TransactionKind.Income,
                cancellationToken);
            if (receipt is null)
            {
                errors.Set(
                    Field(nameof(request.RefundsTransactionId)),
                    ProblemCodes.UnknownReceipt,
                    "refundsTransactionId must be one of your receipts.");
            }
            else if (request.Currency != receipt.Currency)
            {
                errors.Set(
                    Field(nameof(request.RefundsTransactionId)),
                    ProblemCodes.RefundCurrencyMismatch,
                    $"A refund of a {receipt.Currency} receipt must be in {receipt.Currency}.");
            }
            else
            {
                var refundedMinor = await LinkedRefundsMinorAsync(
                    database, userId, receiptId, editedId, cancellationToken);
                if (refundedMinor + request.AmountMinor > receipt.AmountMinor)
                {
                    errors.Set(
                        Field(nameof(request.RefundsTransactionId)),
                        ProblemCodes.RefundExceedsReceipt,
                        $"Refunds linked to this receipt would total {refundedMinor + request.AmountMinor}, "
                        + $"more than its amount {receipt.AmountMinor}.");
                }
            }
        }

        var linkedMinor = row is null
            ? 0
            : await LinkedRefundsMinorAsync(database, userId, row.Id, null, cancellationToken);
        if (row is not null && linkedMinor > 0)
        {
            if (request.Kind != TransactionKind.Income)
            {
                errors.Set(
                    Field(nameof(request.Kind)),
                    ProblemCodes.KindLockedByRefunds,
                    "kind must stay Income while refunds are linked to this receipt.");
            }
            else if (request.Currency != row.Currency)
            {
                errors.Set(
                    Field(nameof(request.Currency)),
                    ProblemCodes.CurrencyLockedByRefunds,
                    "currency must stay the same while refunds are linked to this receipt.");
            }
            else if (request.AmountMinor < linkedMinor)
            {
                errors.Set(
                    Field(nameof(request.AmountMinor)),
                    ProblemCodes.AmountBelowLinkedRefunds,
                    $"amountMinor must be at least {linkedMinor}, the total of the refunds linked to this receipt.");
            }
        }

        // A receipt paying an invoice holds that invoice's number and currency (Rule 14); the link, not
        // an edit, changes them.
        if (row is { InvoiceId: not null })
        {
            var unlinkFirst = $"while the receipt pays invoice {row.InvoiceNumber}; unlink it first.";
            if (request.Kind != TransactionKind.Income)
            {
                errors.Set(
                    Field(nameof(request.Kind)),
                    ProblemCodes.KindLockedByInvoice,
                    $"kind must stay Income {unlinkFirst}");
            }
            else if (request.Currency != row.Currency)
            {
                errors.Set(
                    Field(nameof(request.Currency)),
                    ProblemCodes.CurrencyLockedByInvoice,
                    $"currency must stay {row.Currency} {unlinkFirst}");
            }

            if (Normalize(request).InvoiceNumber != row.InvoiceNumber)
            {
                errors.Set(
                    Field(nameof(request.InvoiceNumber)),
                    ProblemCodes.InvoiceNumberLocked,
                    $"invoiceNumber must stay {row.InvoiceNumber} {unlinkFirst}");
            }
        }

        return errors.OrNull();
    }

    private static Task<long> LinkedRefundsMinorAsync(
        AppDbContext database,
        string userId,
        Guid receiptId,
        Guid? excludedRefundId,
        CancellationToken cancellationToken) =>
        database.Transactions
            .Where(t => t.UserId == userId
                && t.RefundsTransactionId == receiptId
                && t.Id != excludedRefundId)
            .SumAsync(t => t.AmountMinor, cancellationToken);
}
