using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Invoices;

public static class InvoicesEndpoints
{
    private const int MaxFileNameClientLength = 60;

    public static IEndpointRouteBuilder MapInvoicesApi(this IEndpointRouteBuilder routes)
    {
        var invoices = routes.MapGroup("/invoices").WithTags("Invoices").RequireAuthorization();

        invoices.MapGet("", async (
                InvoiceStatus? status,
                Guid? clientId,
                int? year,
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

                var query = database.Invoices.AsNoTracking().Include(invoice => invoice.Client)
                    .Where(invoice => invoice.UserId == user.Id);
                if (status is { } wanted)
                {
                    query = query.Where(invoice => invoice.Status == wanted);
                }

                if (clientId is { } client)
                {
                    query = query.Where(invoice => invoice.ClientId == client);
                }

                if (year is { } issuedIn)
                {
                    query = query.Where(invoice => invoice.IssueDate.Year == issuedIn);
                }

                var rows = await query
                    .OrderByDescending(invoice => invoice.IssueDate)
                    .ThenByDescending(invoice => invoice.NumberSequence)
                    .ThenByDescending(invoice => invoice.CreatedAt)
                    .ToListAsync(cancellationToken);

                var receipts = await InvoicePayments.ReceiptsAsync(
                    database, user.Id, [.. rows.Select(invoice => invoice.Id)], cancellationToken);
                var today = time.TodayInKyiv();

                return Results.Ok(rows
                    .Select(invoice => ToSummary(invoice, InvoicePayments.Balance(invoice, receipts[invoice.Id], today)))
                    .ToArray());
            })
            .Produces<InvoiceSummary[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        invoices.MapGet("/{id:guid}", async (
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

                var invoice = await FindAsync(database, user.Id, id, cancellationToken);

                return invoice is null ? Missing(id) : Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        invoices.MapPost("", async (
                InvoiceRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = request.Normalized();
                if (InvoiceRules.Validate(normalized) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var client = await database.Clients
                    .FirstOrDefaultAsync(row => row.Id == normalized.ClientId && row.UserId == user.Id, cancellationToken);
                if (client is null)
                {
                    return UnknownClient();
                }

                var now = time.GetUtcNow();
                var invoice = new Invoice
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Status = InvoiceStatus.Draft,
                    CreatedAt = now,
                };
                Apply(invoice, normalized, client, now);
                database.Invoices.Add(invoice);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Created($"/api/invoices/{invoice.Id}", await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        invoices.MapPut("/{id:guid}", async (
                Guid id,
                InvoiceRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = request.Normalized();
                if (InvoiceRules.Validate(normalized) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Frozen(invoice, "edited");
                }

                var client = await database.Clients
                    .FirstOrDefaultAsync(row => row.Id == normalized.ClientId && row.UserId == user.Id, cancellationToken);
                if (client is null)
                {
                    return UnknownClient();
                }

                Apply(invoice, normalized, client, time.GetUtcNow());
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        invoices.MapDelete("/{id:guid}", async (
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

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Frozen(invoice, "deleted");
                }

                database.Invoices.Remove(invoice);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        invoices.MapPost("/{id:guid}/duplicate", async (
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

                var source = await FindAsync(database, user.Id, id, cancellationToken);
                if (source is null)
                {
                    return Missing(id);
                }

                // Dated today with the source's payment term, which is what a monthly invoice wants.
                var today = time.TodayInKyiv();
                var now = time.GetUtcNow();
                var copy = new Invoice
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Status = InvoiceStatus.Draft,
                    ClientId = source.ClientId,
                    Client = source.Client,
                    IssueDate = today,
                    DueDate = today.AddDays(source.DueDate.DayNumber - source.IssueDate.DayNumber),
                    Currency = source.Currency,
                    Lines = [.. source.Lines],
                    TotalMinor = source.TotalMinor,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                database.Invoices.Add(copy);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Created($"/api/invoices/{copy.Id}", await ResponseAsync(database, copy, time, cancellationToken));
            })
            .Produces<InvoiceResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                switch (invoice.Status)
                {
                    case InvoiceStatus.Issued:
                        return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
                    case InvoiceStatus.Cancelled:
                        return Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: $"Invoice {invoice.Number} is cancelled and cannot be issued again. Duplicate it instead.");
                }

                var details = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken)
                    ?? new InvoicingDetails { UserId = user.Id };
                var payment = await PaymentDetailsAsync(database, user.Id, invoice.Currency, cancellationToken);
                var missing = InvoiceRules.Completeness(invoice, details, payment, invoice.Client!);
                if (missing.Count > 0)
                {
                    return Results.ValidationProblem(missing, title: "The invoice cannot be issued until the details below are filled in.");
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
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [error] });
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                switch (invoice.Status)
                {
                    case InvoiceStatus.Draft:
                        return Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "A draft has no number to account for. Delete it instead.");
                    case InvoiceStatus.Cancelled:
                        return Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
                }

                // A link always points at an issued invoice, so the receipts leave before the invoice goes.
                if (await database.Transactions.AnyAsync(row => row.InvoiceId == invoice.Id, cancellationToken))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"Invoice {invoice.Number} has receipts linked. Unlink them before cancelling it.");
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
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
                    return Missing(id);
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
            .ProducesProblem(StatusCodes.Status404NotFound);

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
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                var receipt = await database.Transactions
                    .FirstOrDefaultAsync(row => row.Id == receiptId && row.UserId == user.Id, cancellationToken);
                if (receipt is null)
                {
                    return MissingReceipt(receiptId);
                }

                if (receipt.InvoiceId != invoice.Id)
                {
                    var linked = await InvoicePayments.ReceiptsAsync(database, user.Id, [invoice.Id], cancellationToken);
                    if (InvoicePayments.LinkConflict(invoice, receipt, InvoicePayments.PaidMinor(linked[invoice.Id])) is { } conflict)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: conflict);
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
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Missing(id);
                }

                var receipt = await database.Transactions
                    .FirstOrDefaultAsync(row => row.Id == receiptId && row.UserId == user.Id, cancellationToken);
                if (receipt is null)
                {
                    return MissingReceipt(receiptId);
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
            .ProducesProblem(StatusCodes.Status404NotFound);

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
                    return Missing(id);
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
                        row.Client == null ? null : row.Client.Name))
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(options);
            })
            .Produces<ReceiptOption[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Closest due date first: issued, not paid, in the receipt's currency, and its client's, or any
        // client's when the receipt names none.
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
                    return MissingReceipt(receiptId);
                }

                if (receipt is not { Kind: TransactionKind.Income, InvoiceId: null })
                {
                    return Results.Ok(Array.Empty<InvoiceSummary>());
                }

                var candidates = await database.Invoices.AsNoTracking().Include(invoice => invoice.Client)
                    .Where(invoice => invoice.UserId == user.Id
                        && invoice.Status == InvoiceStatus.Issued
                        && invoice.Currency == receipt.Currency
                        && (receipt.ClientId == null || invoice.ClientId == receipt.ClientId))
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
            .ProducesProblem(StatusCodes.Status404NotFound);

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

        return routes;
    }

    /// <summary><c>&lt;number&gt;_&lt;client&gt;.pdf</c> with anything but letters and digits in the client's name turned into one underscore.</summary>
    internal static string FileName(Invoice invoice)
    {
        var client = invoice.Snapshot?.Buyer.Name ?? invoice.Client?.Name ?? string.Empty;
        var safe = new StringBuilder();
        foreach (var c in client)
        {
            if (char.IsLetterOrDigit(c))
            {
                safe.Append(c);
            }
            else if (safe.Length > 0 && safe[^1] != '_')
            {
                safe.Append('_');
            }

            if (safe.Length >= MaxFileNameClientLength)
            {
                break;
            }
        }

        var name = safe.ToString().Trim('_');

        return $"{invoice.Number ?? "draft"}{(name.Length > 0 ? "_" + name : string.Empty)}.pdf";
    }

    private static InvoicePdfModel Model(Invoice invoice, InvoiceSnapshot snapshot, byte[]? signature) => new(
        invoice.Number,
        invoice.Status,
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Currency,
        invoice.Lines,
        invoice.TotalMinor,
        snapshot,
        signature);

    private static void Apply(Invoice invoice, InvoiceRequest request, Transactions.Client client, DateTimeOffset now)
    {
        invoice.ClientId = client.Id;
        invoice.Client = client;
        invoice.IssueDate = request.IssueDate;
        invoice.DueDate = request.DueDate;
        invoice.Currency = request.Currency;
        invoice.Lines = request.ToLines();
        invoice.TotalMinor = InvoiceRules.TotalMinor(invoice.Lines);
        invoice.UpdatedAt = now;
    }

    private static async Task<Invoice?> FindAsync(
        AppDbContext database, string userId, Guid id, CancellationToken cancellationToken, bool tracked = true)
    {
        var query = database.Invoices.Include(invoice => invoice.Client).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(invoice => invoice.Id == id && invoice.UserId == userId, cancellationToken);
    }

    private static Task<InvoicingPaymentDetails?> PaymentDetailsAsync(
        AppDbContext database, string userId, Currency currency, CancellationToken cancellationToken) =>
        database.InvoicingPaymentDetails.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Currency == currency, cancellationToken);

    private static IResult Missing(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No invoice exists with id {id}.");

    private static IResult UnknownClient() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["clientId"] = ["clientId must be one of your clients."] });

    private static IResult Frozen(Invoice invoice, string verb) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: $"Invoice {invoice.Number} is {invoice.Status.ToString().ToLowerInvariant()} and cannot be {verb}."
            + (invoice.Status == InvoiceStatus.Issued ? " Cancel it with a reason, or duplicate it as a new draft." : string.Empty));

    private static string ClientName(Invoice invoice) => invoice.Snapshot?.Buyer.Name ?? invoice.Client?.Name ?? string.Empty;

    private static IResult MissingReceipt(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No transaction exists with id {id}.");

    private static async Task<InvoiceResponse> ResponseAsync(
        AppDbContext database, Invoice invoice, TimeProvider time, CancellationToken cancellationToken)
    {
        var receipts = await InvoicePayments.ReceiptsAsync(database, invoice.UserId, [invoice.Id], cancellationToken);
        LinkedReceipt[] linked = [.. receipts[invoice.Id]];

        return ToResponse(invoice, InvoicePayments.Balance(invoice, linked, time.TodayInKyiv()), linked);
    }

    internal static InvoiceSummary ToSummary(Invoice invoice, InvoiceBalance balance) => new(
        invoice.Id,
        invoice.Status,
        balance.Standing,
        invoice.Number,
        invoice.ClientId,
        ClientName(invoice),
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Currency,
        invoice.TotalMinor,
        balance.PaidMinor,
        balance.DueMinor);

    private static InvoiceResponse ToResponse(Invoice invoice, InvoiceBalance balance, LinkedReceipt[] receipts) => new(
        invoice.Id,
        invoice.Status,
        balance.Standing,
        invoice.Number,
        invoice.ClientId,
        ClientName(invoice),
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Currency,
        [.. invoice.Lines.Select(line => new InvoiceLineResponse(
            line.DescriptionEn,
            line.DescriptionUk,
            line.Unit,
            line.QuantityThousandths,
            line.RateMinor,
            InvoiceRules.LineAmountMinor(line.QuantityThousandths, line.RateMinor)))],
        invoice.TotalMinor,
        balance.PaidMinor,
        balance.DueMinor,
        receipts,
        invoice.CancelReason,
        invoice.IssuedAt,
        invoice.CancelledAt,
        FileName(invoice));
}

/// <summary>
/// <c>Status</c> is the stored lifecycle; <c>Standing</c> is what the owner sees, an issued invoice read
/// as paid or overdue from its linked receipts (Rule 14).
/// </summary>
internal sealed record InvoiceSummary(
    Guid Id,
    InvoiceStatus Status,
    InvoiceStanding Standing,
    string? Number,
    Guid ClientId,
    string ClientName,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    long TotalMinor,
    long PaidMinor,
    long? DueMinor);

internal sealed record InvoiceLineResponse(
    string DescriptionEn,
    string DescriptionUk,
    InvoiceUnit Unit,
    long QuantityThousandths,
    long RateMinor,
    long AmountMinor);

internal sealed record InvoiceResponse(
    Guid Id,
    InvoiceStatus Status,
    InvoiceStanding Standing,
    string? Number,
    Guid ClientId,
    string ClientName,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLineResponse[] Lines,
    long TotalMinor,
    long PaidMinor,
    long? DueMinor,
    LinkedReceipt[] Receipts,
    string? CancelReason,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    string PdfFileName);
