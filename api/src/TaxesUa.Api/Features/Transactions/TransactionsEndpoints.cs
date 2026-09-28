using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Transactions;

public static class TransactionsEndpoints
{
    internal const int MinYear = 2000;

    internal const int MaxYear = 2100;

    // Caps the hryvnia result as well as the amount. Money.ToUahKop multiplies AmountMinor by RateE4
    // unchecked, and a result of at most 1e14 kop bounds that product by 1e14 x 10^4, inside long. 1e14
    // also stays inside JS Number.MAX_SAFE_INTEGER, which is what the web reads both as.
    private const long MaxAmountMinor = 100_000_000_000_000;

    // 1000.0000 UAH per unit.
    private const int MaxRateE4 = 10_000_000;

    private const int MaxReasonLength = 1000;

    internal const int MaxClientNameLength = 200;

    private const int MaxInvoiceNumberLength = 100;

    private const int MaxDescriptionLength = 1000;

    public static IEndpointRouteBuilder MapTransactionsApi(this IEndpointRouteBuilder routes)
    {
        var transactions = routes.MapGroup("/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

        transactions.MapGet("", async (
                int year,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (year < MinYear || year > MaxYear)
                {
                    return Results.ValidationProblem(YearOutOfRange());
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);

                var rows = await database.Transactions
                    .Include(row => row.Client)
                    .Include(row => row.RefundsTransaction)
                    .Where(row => row.UserId == user.Id
                        && row.ValueDate >= new DateOnly(year, 1, 1)
                        && row.ValueDate < new DateOnly(year + 1, 1, 1))
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .ToListAsync(cancellationToken);

                var totalIncomeKop = IncomeLedger.ForYear(
                    year, rows.Select(row => row.ToEngineInput()).ToList(), settings.ToEngineInput()).TotalIncomeKop;
                var items = rows
                    .Select(row => ToResponse(row, row.Client?.Name, IsBeforeRegistration(row, settings)))
                    .ToArray();

                return Results.Ok(new TransactionListResponse(
                    year, settings.FopRegistrationDate, totalIncomeKop, items));
            })
            .Produces<TransactionListResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        transactions.MapPost("", async (
                TransactionRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                FxRates rates,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // Rejects a malformed body before touching the user store, same as before the create
                // path moved into TransactionRecorder.
                var normalized = Normalize(request);
                if (Validate(request, normalized, time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var result = await TransactionRecorder.RecordAsync(
                    database, user.Id, request, rates, time.TodayInKyiv(), cancellationToken);

                return result switch
                {
                    RecordTransactionResult.Success success => Results.Created(
                        $"/api/transactions/{success.Row.Id}",
                        ToResponse(success.Row, success.ClientName, success.BeforeRegistration)),
                    RecordTransactionResult.Invalid invalid => Results.ValidationProblem(invalid.Errors),
                    RecordTransactionResult.RateUnavailable unavailable =>
                        FxEndpoints.RateUnavailable(unavailable.Currency, unavailable.Date, unavailable.Lookup),
                    _ => throw new UnreachableException(),
                };
            })
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        transactions.MapPut("/{id:guid}", async (
                Guid id,
                TransactionRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                FxRates rates,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = Normalize(request);
                if (Validate(request, normalized, time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var row = await database.Transactions
                    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Missing(id);
                }

                if (await ValidateLinksAsync(database, user.Id, row, request, cancellationToken) is { } linkErrors)
                {
                    return Results.ValidationProblem(linkErrors);
                }

                if (await ApplyAmountAsync(row, request, rates, cancellationToken) is { } problem)
                {
                    return problem switch
                    {
                        AmountProblem.Invalid invalid => Results.ValidationProblem(invalid.Errors),
                        AmountProblem.RateUnavailable unavailable =>
                            FxEndpoints.RateUnavailable(unavailable.Currency, unavailable.Date, unavailable.Lookup),
                        _ => throw new UnreachableException(),
                    };
                }

                row.ClientId = await ResolveClientAsync(database, user.Id, normalized.ClientName, cancellationToken);
                row.Kind = request.Kind;
                row.NonIncomeReason = normalized.NonIncomeReason;
                row.RefundsTransactionId = request.RefundsTransactionId;
                row.RefundsTransaction = await FindReceiptAsync(database, request, cancellationToken);
                row.InvoiceNumber = normalized.InvoiceNumber;
                row.Description = normalized.Description;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                var beforeRegistration = IsBeforeRegistration(row, settings);

                return Results.Ok(ToResponse(row, normalized.ClientName, beforeRegistration));
            })
            .Produces<TransactionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        transactions.MapDelete("/{id:guid}", async (
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

                var row = await database.Transactions
                    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Missing(id);
                }

                if (await database.Transactions.AnyAsync(
                        t => t.UserId == user.Id && t.RefundsTransactionId == row.Id, cancellationToken))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "This receipt has linked refunds. Delete or unlink them first.");
                }

                database.Transactions.Remove(row);
                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        transactions.MapGet("/receipts", async (
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

                var receipts = await database.Transactions
                    .Where(row => row.UserId == user.Id && row.Kind == TransactionKind.Income)
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .Select(row => new ReceiptOption(
                        row.Id,
                        row.ValueDate,
                        row.AmountMinor,
                        row.Currency,
                        row.Client == null ? null : row.Client.Name))
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(receipts);
            })
            .Produces<ReceiptOption[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        routes.MapGroup("/clients")
            .WithTags("Transactions")
            .RequireAuthorization()
            .MapGet("", async (
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

                var names = await database.Clients
                    .Where(client => client.UserId == user.Id)
                    .OrderBy(client => client.Name)
                    .Select(client => client.Name)
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(names);
            })
            .Produces<string[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static IResult Missing(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No transaction exists with id {id}.");

    // Asks the same Rule 8 decision `IncomeLedger.ForYear` makes, so a row's flag and the list's total
    // cannot disagree.
    internal static bool IsBeforeRegistration(Transaction row, SettingsEntity settings) =>
        settings.FopRegistrationDate is { } registrationDate
        && IncomeLedger.Exclusion(row.ToEngineInput(), registrationDate) is not null;

    // Validation already loaded the receipt into the context, so this is a lookup, not a query.
    internal static async Task<Transaction?> FindReceiptAsync(
        AppDbContext database, TransactionRequest request, CancellationToken cancellationToken) =>
        request.RefundsTransactionId is { } receiptId
            ? await database.Transactions.FindAsync([receiptId], cancellationToken)
            : null;

    // Rule 8 follows a refund to its receipt, so a link has to point at a receipt that can carry it:
    // the owner's own Income row in the refund's currency, not over-refunded (compared in that
    // currency's minor units), and not turned into something else later.
    internal static async Task<Dictionary<string, string[]>?> ValidateLinksAsync(
        AppDbContext database,
        string userId,
        Transaction? row,
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
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
                errors[Field(nameof(request.RefundsTransactionId))] =
                    ["refundsTransactionId must be one of your receipts."];
            }
            else if (request.Currency != receipt.Currency)
            {
                errors[Field(nameof(request.RefundsTransactionId))] =
                    [$"A refund of a {receipt.Currency} receipt must be in {receipt.Currency}."];
            }
            else
            {
                var refundedMinor = await LinkedRefundsMinorAsync(
                    database, userId, receiptId, editedId, cancellationToken);
                if (refundedMinor + request.AmountMinor > receipt.AmountMinor)
                {
                    errors[Field(nameof(request.RefundsTransactionId))] =
                    [
                        $"Refunds linked to this receipt would total {refundedMinor + request.AmountMinor}, "
                        + $"more than its amount {receipt.AmountMinor}.",
                    ];
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
                errors[Field(nameof(request.Kind))] =
                    ["kind must stay Income while refunds are linked to this receipt."];
            }
            else if (request.Currency != row.Currency)
            {
                errors[Field(nameof(request.Currency))] =
                    ["currency must stay the same while refunds are linked to this receipt."];
            }
            else if (request.AmountMinor < linkedMinor)
            {
                errors[Field(nameof(request.AmountMinor))] =
                    [$"amountMinor must be at least {linkedMinor}, the total of the refunds linked to this receipt."];
            }
        }

        return errors.Count == 0 ? null : errors;
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

    // The one place POST and PUT fix the rate (Rule 2), so the two cannot diverge. It runs before any
    // other change to the row or the context, since FxRates saves its cache row with its own
    // SaveChanges. On PUT, row still holds the stored values it compares against.
    internal static async Task<AmountProblem?> ApplyAmountAsync(
        Transaction row, TransactionRequest request, FxRates rates, CancellationToken cancellationToken)
    {
        (int RateE4, DateOnly? RateDate, RateSource? Source) rate;
        if (request.ManualRateE4 is { } manualRateE4)
        {
            rate = (manualRateE4, null, RateSource.Manual);
        }
        else if (request.Currency == Currency.UAH)
        {
            rate = (Money.RateScale, null, null);
        }
        else if (row.RateSource == RateSource.Nbu
            && row.Currency == request.Currency
            && row.ValueDate == request.ValueDate)
        {
            // The rate is fixed when recorded, so an edit of the other fields must not move it.
            rate = (row.RateE4, row.RateDate, RateSource.Nbu);
        }
        else
        {
            var lookup = await rates.GetAsync(request.Currency, request.ValueDate, cancellationToken);
            if (lookup is not NbuLookup.Found found)
            {
                return new AmountProblem.RateUnavailable(request.Currency, request.ValueDate, lookup);
            }

            rate = (found.RateE4, found.RateDate, RateSource.Nbu);
        }

        if (ExceedsUahBound(request.AmountMinor, rate.RateE4))
        {
            return new AmountProblem.Invalid(new Dictionary<string, string[]>
            {
                [Field(nameof(request.AmountMinor))] =
                    [$"amountMinor at this rate must not exceed {MaxAmountMinor} kopecks in hryvnia."],
            });
        }

        row.ValueDate = request.ValueDate;
        row.AmountMinor = request.AmountMinor;
        row.Currency = request.Currency;
        row.RateE4 = rate.RateE4;
        row.RateDate = rate.RateDate;
        row.RateSource = rate.Source;
        row.AmountUahKop = Money.ToUahKop(request.AmountMinor, rate.RateE4);

        return null;
    }

    internal static bool ExceedsUahBound(long amountMinor, int rateE4) =>
        (Int128)amountMinor * rateE4 > (Int128)MaxAmountMinor * Money.RateScale;

    internal static async Task<Guid?> ResolveClientAsync(
        AppDbContext database, string userId, string? name, CancellationToken cancellationToken)
    {
        if (name is null)
        {
            return null;
        }

        var existingId = await database.Clients
            .Where(client => client.UserId == userId && client.Name == name)
            .Select(client => (Guid?)client.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId;
        }

        var client = new Client { Id = Guid.NewGuid(), UserId = userId, Name = name };
        database.Clients.Add(client);

        return client.Id;
    }

    internal static NormalizedText Normalize(TransactionRequest request) => new(
        Trim(request.NonIncomeReason),
        Trim(request.ClientName),
        Trim(request.InvoiceNumber),
        Trim(request.Description));

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static TransactionResponse ToResponse(Transaction row, string? clientName, bool beforeRegistration) =>
        new(
            row.Id,
            row.ValueDate,
            row.AmountMinor,
            row.Currency,
            row.RateE4,
            row.RateDate,
            row.RateSource,
            row.AmountUahKop,
            row.Kind,
            row.NonIncomeReason,
            clientName,
            row.InvoiceNumber,
            row.Description,
            beforeRegistration,
            row.RefundsTransaction is { } receipt
                ? new RefundedReceipt(receipt.Id, receipt.ValueDate, receipt.AmountMinor, receipt.Currency)
                : null);

    private static Dictionary<string, string[]> YearOutOfRange() => new()
    {
        ["year"] = [$"year must be between {MinYear} and {MaxYear}."],
    };

    internal static Dictionary<string, string[]>? Validate(
        TransactionRequest request, NormalizedText normalized, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.AmountMinor <= 0)
        {
            errors[Field(nameof(request.AmountMinor))] = ["amountMinor must be positive."];
        }
        else if (request.AmountMinor > MaxAmountMinor)
        {
            errors[Field(nameof(request.AmountMinor))] = [$"amountMinor must not exceed {MaxAmountMinor}."];
        }

        if (request.ManualRateE4 is { } manualRateE4)
        {
            if (request.Currency == Currency.UAH)
            {
                errors[Field(nameof(request.ManualRateE4))] = ["manualRateE4 must be empty for UAH."];
            }
            else if (manualRateE4 < 1 || manualRateE4 > MaxRateE4)
            {
                errors[Field(nameof(request.ManualRateE4))] = [$"manualRateE4 must be between 1 and {MaxRateE4}."];
            }
        }

        if (request.ValueDate.Year < MinYear || request.ValueDate.Year > MaxYear)
        {
            errors[Field(nameof(request.ValueDate))] =
                [$"valueDate year must be between {MinYear} and {MaxYear}."];
        }
        else if (request.ValueDate > today)
        {
            // Income arises on the credit date (Rule 2), so a real operation cannot be dated after
            // today in Kyiv.
            errors[Field(nameof(request.ValueDate))] = ["valueDate must not be after today."];
        }

        var isIncomeKind = request.Kind is TransactionKind.Income or TransactionKind.RefundToClient;
        if (!isIncomeKind && normalized.NonIncomeReason is null)
        {
            errors[Field(nameof(request.NonIncomeReason))] =
                ["nonIncomeReason is required for a non-income kind."];
        }
        else if (isIncomeKind && normalized.NonIncomeReason is not null)
        {
            errors[Field(nameof(request.NonIncomeReason))] =
                ["nonIncomeReason must be empty for an income kind."];
        }
        else if (normalized.NonIncomeReason is { Length: > MaxReasonLength })
        {
            errors[Field(nameof(request.NonIncomeReason))] =
                [$"nonIncomeReason must not exceed {MaxReasonLength} characters."];
        }
        else if (normalized.NonIncomeReason is { } reason && TextRules.HasDisallowedControlChar(reason))
        {
            errors[Field(nameof(request.NonIncomeReason))] = [ControlCharMessage("nonIncomeReason")];
        }

        if (request.RefundsTransactionId is not null && request.Kind != TransactionKind.RefundToClient)
        {
            errors[Field(nameof(request.RefundsTransactionId))] =
                ["refundsTransactionId is allowed only on a refund to a client."];
        }

        if (normalized.ClientName is { Length: > MaxClientNameLength })
        {
            errors[Field(nameof(request.ClientName))] =
                [$"clientName must not exceed {MaxClientNameLength} characters."];
        }
        else if (normalized.ClientName is { } clientName && TextRules.HasDisallowedControlChar(clientName))
        {
            errors[Field(nameof(request.ClientName))] = [ControlCharMessage("clientName")];
        }

        if (normalized.InvoiceNumber is { Length: > MaxInvoiceNumberLength })
        {
            errors[Field(nameof(request.InvoiceNumber))] =
                [$"invoiceNumber must not exceed {MaxInvoiceNumberLength} characters."];
        }
        else if (normalized.InvoiceNumber is { } invoiceNumber && TextRules.HasDisallowedControlChar(invoiceNumber))
        {
            errors[Field(nameof(request.InvoiceNumber))] = [ControlCharMessage("invoiceNumber")];
        }

        if (normalized.Description is { Length: > MaxDescriptionLength })
        {
            errors[Field(nameof(request.Description))] =
                [$"description must not exceed {MaxDescriptionLength} characters."];
        }
        else if (normalized.Description is { } description && TextRules.HasDisallowedControlChar(description))
        {
            errors[Field(nameof(request.Description))] = [ControlCharMessage("description")];
        }

        return errors.Count == 0 ? null : errors;
    }

    // Derived rather than spelled a second time, so the key the web reads an error under cannot drift
    // from the member it is about. CamelCase is the policy JsonSerializerDefaults.Web applies to the
    // same member.
    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string ControlCharMessage(string field) =>
        $"{field} must not contain a NUL or other control character (tab, line feed and carriage return are allowed).";

    internal readonly record struct NormalizedText(
        string? NonIncomeReason,
        string? ClientName,
        string? InvoiceNumber,
        string? Description);
}

internal sealed record TransactionRequest(
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    int? ManualRateE4,
    TransactionKind Kind,
    string? NonIncomeReason,
    string? ClientName,
    string? InvoiceNumber,
    string? Description,
    Guid? RefundsTransactionId);

internal sealed record TransactionResponse(
    Guid Id,
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    int RateE4,
    DateOnly? RateDate,
    RateSource? RateSource,
    long AmountUahKop,
    TransactionKind Kind,
    string? NonIncomeReason,
    string? ClientName,
    string? InvoiceNumber,
    string? Description,
    bool BeforeRegistration,
    RefundedReceipt? RefundsReceipt);

internal sealed record RefundedReceipt(Guid Id, DateOnly ValueDate, long AmountMinor, Currency Currency);

internal sealed record ReceiptOption(
    Guid Id,
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    string? ClientName);

internal sealed record TransactionListResponse(
    int Year,
    DateOnly? FopRegistrationDate,
    long TotalIncomeKop,
    TransactionResponse[] Items);
