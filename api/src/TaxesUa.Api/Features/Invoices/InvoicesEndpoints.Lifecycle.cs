using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Invoices;

public static partial class InvoicesEndpoints
{
    private static void MapLifecycleEndpoints(RouteGroupBuilder invoices)
    {
        invoices.MapPost("/{id:guid}/issue", async (
                Guid id,
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

                // The lock makes max-plus-one safe: a second issue of the same owner waits here until
                // this one's number is committed, then reads it.
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
                }

                switch (invoice.Status)
                {
                    case InvoiceStatus.Issued:
                        return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
                    case InvoiceStatus.Cancelled:
                        return Problems.Create(
                            StatusCodes.Status409Conflict,
                            ProblemCodes.InvoiceCancelled,
                            $"Invoice {invoice.Number} is cancelled and cannot be issued again. Duplicate it instead.");
                }

                var details = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken)
                    ?? new InvoicingDetails { UserId = user.Id };
                var payment = await PaymentDetailsAsync(database, user.Id, invoice.Currency, cancellationToken);
                var missing = InvoiceRules.Completeness(invoice, details, payment, invoice.Client!);
                if (missing.Count > 0)
                {
                    return Problems.Validation(
                        missing,
                        "The invoice cannot be issued until the details below are filled in.",
                        code: ProblemCodes.InvoiceIncomplete);
                }

                var year = invoice.IssueDate.Year;
                var last = await database.Invoices
                    .Where(row => row.UserId == user.Id && row.NumberYear == year)
                    .MaxAsync(row => row.NumberSequence, cancellationToken);
                var now = time.GetUtcNow();
                invoice.NumberYear = year;
                invoice.NumberSequence = (last ?? 0) + 1;
                invoice.Snapshot = InvoiceRules.Snapshot(details, payment, invoice.Client!);
                invoice.SignatureImage = details.SignatureImage;
                invoice.SignatureContentType = details.SignatureContentType;
                invoice.Status = InvoiceStatus.Issued;
                invoice.IssuedAt = now;
                invoice.UpdatedAt = now;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        invoices.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelInvoiceRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var reason = request.Reason?.Trim() ?? string.Empty;
                if (InvoiceRules.CancelReasonError(reason) is { } error)
                {
                    return Problems.Validation("reason", error.Code, error.Message);
                }

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

                switch (invoice.Status)
                {
                    case InvoiceStatus.Draft:
                        return Problems.Create(
                            StatusCodes.Status409Conflict,
                            ProblemCodes.DraftHasNoNumber,
                            "A draft has no number to account for. Delete it instead.");
                    case InvoiceStatus.Cancelled:
                        return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
                }

                // A link always points at an issued invoice, so the receipts leave before the invoice goes.
                if (await database.Transactions.AnyAsync(row => row.InvoiceId == invoice.Id, cancellationToken))
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.InvoiceHasReceipts,
                        $"Invoice {invoice.Number} has receipts linked. Unlink them before cancelling it.");
                }

                var now = time.GetUtcNow();
                invoice.Status = InvoiceStatus.Cancelled;
                invoice.CancelReason = reason;
                invoice.CancelledAt = now;
                invoice.UpdatedAt = now;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        invoices.MapGet("/{id:guid}/pdf", async (
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

                InvoicePdfModel model;
                if (invoice is { Snapshot: { } snapshot })
                {
                    model = Model(invoice, snapshot, invoice.SignatureImage);
                }
                else
                {
                    var details = await database.InvoicingDetails.AsNoTracking()
                        .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken)
                        ?? new InvoicingDetails { UserId = user.Id };
                    var payment = await PaymentDetailsAsync(database, user.Id, invoice.Currency, cancellationToken);
                    model = Model(invoice, InvoiceRules.Snapshot(details, payment, invoice.Client!), details.SignatureImage);
                }

                // Inline, so a preview opens in the browser's viewer; the download link asks for a
                // download and the browser keeps this name.
                var disposition = new ContentDispositionHeaderValue("inline");
                disposition.SetHttpFileName(FileName(invoice));
                http.Response.Headers.ContentDisposition = disposition.ToString();
                http.Response.Headers.CacheControl = "private, no-store";

                return Results.File(InvoicePdf.ToPdf(model), "application/pdf");
            })
            .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf")
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);
    }
}
