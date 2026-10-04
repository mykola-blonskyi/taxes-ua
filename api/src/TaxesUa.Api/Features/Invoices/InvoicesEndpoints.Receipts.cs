using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Invoices;

public static partial class InvoicesEndpoints
{
    private static void MapReceiptEndpoints(RouteGroupBuilder invoices)
    {
        invoices.MapPost("/{id:guid}/receipts/{receiptId:guid}", async (
                Guid id,
                Guid receiptId,
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

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
                }

                var receipt = await database.Transactions
                    .FirstOrDefaultAsync(row => row.Id == receiptId && row.UserId == user.Id, cancellationToken);
                if (receipt is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", receiptId);
                }

                if (receipt.InvoiceId != invoice.Id)
                {
                    var linked = await InvoicePayments.ReceiptsAsync(database, user.Id, [invoice.Id], cancellationToken);
                    if (InvoicePayments.LinkConflict(invoice, receipt, InvoicePayments.PaidMinor(linked[invoice.Id])) is { } conflict)
                    {
                        return Problems.Create(StatusCodes.Status409Conflict, conflict.Code, conflict.Message);
                    }

                    // Linking is the owner's word on what the money is, so it reviews an imported receipt
                    // and a sync can no longer move its kind.
                    receipt.InvoiceId = invoice.Id;
                    receipt.InvoiceNumber = invoice.Number;
                    receipt.ClientId ??= invoice.ClientId;
                    receipt.ReviewStatus = ReviewStatus.Confirmed;
                    receipt.UpdatedAt = time.GetUtcNow();
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        invoices.MapDelete("/{id:guid}/receipts/{receiptId:guid}", async (
                Guid id,
                Guid receiptId,
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

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
                }

                var receipt = await database.Transactions
                    .FirstOrDefaultAsync(row => row.Id == receiptId && row.UserId == user.Id, cancellationToken);
                if (receipt is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", receiptId);
                }

                if (receipt.InvoiceId == invoice.Id)
                {
                    receipt.InvoiceId = null;
                    receipt.InvoiceNumber = null;
                    receipt.UpdatedAt = time.GetUtcNow();
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        // Newest first: unlinked and in the invoice's currency, whatever its client, since a client may pay
        // through an intermediary; the picker warns when the payer is not the invoice's client.
        invoices.MapGet("/{id:guid}/receipt-options", async (
                Guid id,
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

                var invoice = await FindAsync(database, user.Id, id, cancellationToken, tracked: false);
                if (invoice is null)
                {
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
                }

                if (invoice.Status != InvoiceStatus.Issued)
                {
                    return Results.Ok(Array.Empty<ReceiptOption>());
                }

                var options = await database.Transactions.AsNoTracking()
                    .Where(row => row.UserId == user.Id
                        && row.Kind == TransactionKind.Income
                        && row.InvoiceId == null
                        && row.Currency == invoice.Currency)
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .Select(row => new ReceiptOption(
                        row.Id,
                        row.ValueDate,
                        row.AmountMinor,
                        row.Currency,
                        row.Client == null ? null : row.Client.Name,
                        row.ClientId))
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(options);
            })
            .Produces<ReceiptOption[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        // Closest due date first: issued, not paid and in the receipt's currency, whatever the client, since a
        // client may pay through an intermediary; the screen warns when the payer is not the invoice's client.
        invoices.MapGet("/payable-by/{receiptId:guid}", async (
                Guid receiptId,
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

                var receipt = await database.Transactions.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.Id == receiptId && row.UserId == user.Id, cancellationToken);
                if (receipt is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", receiptId);
                }

                if (receipt is not { Kind: TransactionKind.Income, InvoiceId: null })
                {
                    return Results.Ok(Array.Empty<InvoiceSummary>());
                }

                var candidates = await database.Invoices.AsNoTracking().Include(invoice => invoice.Client)
                    .Where(invoice => invoice.UserId == user.Id
                        && invoice.Status == InvoiceStatus.Issued
                        && invoice.Currency == receipt.Currency)
                    .OrderBy(invoice => invoice.DueDate)
                    .ThenBy(invoice => invoice.NumberYear)
                    .ThenBy(invoice => invoice.NumberSequence)
                    .ToListAsync(cancellationToken);
                var receipts = await InvoicePayments.ReceiptsAsync(
                    database, user.Id, [.. candidates.Select(invoice => invoice.Id)], cancellationToken);
                var today = time.TodayInKyiv();

                return Results.Ok(candidates
                    .Select(invoice => ToSummary(invoice, InvoicePayments.Balance(invoice, receipts[invoice.Id], today)))
                    .Where(summary => summary.Standing != InvoiceStanding.Paid)
                    .ToArray());
            })
            .Produces<InvoiceSummary[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        // Only the review queue: a suggestion is for a receipt the owner has not yet settled, and
        // confirming the receipt unlinked is what dismisses it for good.
        invoices.MapGet("/suggestions", async (
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

                return Results.Ok(await InvoiceSuggestions.ForReviewAsync(
                    database, user.Id, time.TodayInKyiv(), ToSummary, cancellationToken));
            })
            .Produces<InvoiceSuggestion[]>()
            .Produces(StatusCodes.Status401Unauthorized);
    }
}
