using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Invoices;

public static partial class InvoicesEndpoints
{
    private const int MaxFileNameClientLength = 60;
    public static IEndpointRouteBuilder MapInvoicesApi(this IEndpointRouteBuilder routes)
    {
        var invoices = routes.MapGroup("/invoices").WithTags("Invoices").RequireAuthorization();

        MapDraftEndpoints(invoices);
        MapLifecycleEndpoints(invoices);
        MapReceiptEndpoints(invoices);

        return routes;
    }

    private static void MapDraftEndpoints(RouteGroupBuilder invoices)
    {
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

                return invoice is null ? Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id) : Results.Ok(await ResponseAsync(database, invoice, time, cancellationToken));
            })
            .Produces<InvoiceResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

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
                    return Problems.Validation(errors);
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
            .ProducesFieldProblem()
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
                    return Problems.Validation(errors);
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

                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Frozen(invoice, ProblemCodes.InvoiceNotEditable, "edited");
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
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

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
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var invoice = await FindAsync(database, user.Id, id, cancellationToken);
                if (invoice is null)
                {
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
                }

                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Frozen(invoice, ProblemCodes.InvoiceNotDeletable, "deleted");
                }

                database.Invoices.Remove(invoice);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

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
                    return Problems.NotFound(ProblemCodes.InvoiceNotFound, "invoice", id);
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
            .ProducesCodedProblem(StatusCodes.Status404NotFound);
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

    private static IResult UnknownClient() =>
        Problems.Validation("clientId", ProblemCodes.UnknownClient, "clientId must be one of your clients.");

    private static IResult Frozen(Invoice invoice, string code, string verb) => Problems.Create(
        StatusCodes.Status409Conflict,
        code,
        $"Invoice {invoice.Number} is {invoice.Status.ToString().ToLowerInvariant()} and cannot be {verb}."
            + (invoice.Status == InvoiceStatus.Issued ? " Cancel it with a reason, or duplicate it as a new draft." : string.Empty));

    internal static string ClientName(Invoice invoice) => invoice.Snapshot?.Buyer.Name ?? invoice.Client?.Name ?? string.Empty;

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
