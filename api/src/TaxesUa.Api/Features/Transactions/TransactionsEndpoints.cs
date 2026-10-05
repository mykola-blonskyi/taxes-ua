using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Transactions;

public static partial class TransactionsEndpoints
{
    // Caps the hryvnia result as well as the amount. Money.ToUahKop multiplies AmountMinor by RateE4
    // unchecked, and a result of at most 1e14 kop bounds that product by 1e14 x 10^4, inside long. 1e14
    // also stays inside JS Number.MAX_SAFE_INTEGER, which is what the web reads both as.
    private const long MaxAmountMinor = 100_000_000_000_000;

    // 1000.0000 UAH per unit.
    private const int MaxRateE4 = 10_000_000;

    private const int MaxReasonLength = 1000;

    private const int MaxInvoiceNumberLength = 100;

    internal const int MaxDescriptionLength = 1000;

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
                if (year < Limits.MinYear || year > Limits.MaxYear)
                {
                    return Problems.Validation(
                        "year", ProblemCodes.YearOutOfRange, $"year must be between {Limits.MinYear} and {Limits.MaxYear}.");
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
                    .Include(row => row.BankAccount)
                    .Where(row => row.UserId == user.Id
                        && row.ValueDate >= new DateOnly(year, 1, 1)
                        && row.ValueDate < new DateOnly(year + 1, 1, 1))
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .ThenBy(row => row.Id)
                    .ToListAsync(cancellationToken);

                var totalIncomeKop = IncomeLedger.ForYear(
                    year, rows.Select(row => row.ToEngineInput()).ToList(), settings.ToEngineInput()).TotalIncomeKop;
                var setAside = await SetAsideOfAsync(database, settings, rows, cancellationToken);
                var items = rows
                    .Select(row => ToResponse(row, row.Client?.Name, IsBeforeRegistration(row, settings), setAside(row)))
                    .ToArray();

                return Results.Ok(new TransactionListResponse(
                    year, settings.FopRegistrationDate, totalIncomeKop, items));
            })
            .Produces<TransactionListResponse>()
            .ProducesFieldProblem()
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
                // A malformed body answers 400 before the user lookup can answer 401.
                var normalized = Normalize(request);
                if (Validate(request, normalized, time.TodayInKyiv()) is { } errors)
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
                var result = await TransactionRecorder.RecordAsync(
                    database, user.Id, request, provenance: null, rates, time.TodayInKyiv(), time, cancellationToken);

                if (result is RecordTransactionResult.Success recorded)
                {
                    await transaction.CommitAsync(cancellationToken);
                    var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                    var setAside = await SetAsideOfAsync(database, settings, [recorded.Row], cancellationToken);

                    return Results.Created(
                        $"/api/transactions/{recorded.Row.Id}",
                        ToResponse(recorded.Row, recorded.ClientName, recorded.BeforeRegistration, setAside(recorded.Row)));
                }

                return result switch
                {
                    RecordTransactionResult.Invalid invalid => Problems.Validation(invalid.Errors),
                    RecordTransactionResult.RateUnavailable unavailable =>
                        FxEndpoints.RateUnavailable(unavailable.Currency, unavailable.Date, unavailable.Lookup),
                    _ => throw new UnreachableException(),
                };
            })
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status502BadGateway);

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
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var lookup = await LookUpRateAsync(request, rates, cancellationToken);
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var row = await database.Transactions
                    .Include(t => t.BankAccount)
                    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", id);
                }

                if (await ValidateLinksAsync(database, user.Id, row, request, cancellationToken) is { } linkErrors)
                {
                    return Problems.Validation(linkErrors);
                }

                if (ApplyAmount(row, request, lookup) is { } problem)
                {
                    return problem switch
                    {
                        AmountProblem.Invalid invalid => Problems.Validation(invalid.Errors),
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
                row.ReviewStatus = ReviewStatus.Confirmed;
                row.UpdatedAt = time.GetUtcNow();
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                var beforeRegistration = IsBeforeRegistration(row, settings);
                var setAside = await SetAsideOfAsync(database, settings, [row], cancellationToken);

                return Results.Ok(ToResponse(row, normalized.ClientName, beforeRegistration, setAside(row)));
            })
            .Produces<TransactionResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status502BadGateway);

        transactions.MapDelete("/{id:guid}", async (
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

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await OwnerLock.AcquireAsync(database, user.Id, cancellationToken);
                var row = await database.Transactions
                    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", id);
                }

                if (await database.Transactions.AnyAsync(
                        t => t.UserId == user.Id && t.RefundsTransactionId == row.Id, cancellationToken))
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.ReceiptHasRefunds,
                        "This receipt has linked refunds. Delete or unlink them first.");
                }

                // An imported row stays as a tombstone holding its operation id, so the next sync does
                // not record the operation again. It counts nowhere, so it drops its refund and invoice
                // links: a hidden link would slip past every refund check and keep an invoice paid.
                // Either way an invoice the receipt paid reopens.
                if (row.ExternalId is null)
                {
                    database.Transactions.Remove(row);
                }
                else
                {
                    row.ReviewStatus = ReviewStatus.Dismissed;
                    row.RefundsTransactionId = null;
                    if (row.InvoiceId is not null)
                    {
                        row.InvoiceId = null;
                        row.InvoiceNumber = null;
                    }

                    row.UpdatedAt = time.GetUtcNow();
                }

                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        transactions.MapPost("/{id:guid}/confirm", async (
                Guid id,
                ConfirmRequest request,
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
                var row = await database.Transactions
                    .Include(t => t.Client)
                    .Include(t => t.RefundsTransaction)
                    .Include(t => t.BankAccount)
                    .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Problems.NotFound(ProblemCodes.TransactionNotFound, "transaction", id);
                }

                // A sync may have moved the suggestion since the owner read it; confirming then would save
                // a kind they never saw.
                if (row.Kind != request.Kind)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.TransactionKindChanged,
                        $"The transaction is now {row.Kind}, not {request.Kind}. Reload it before confirming.");
                }

                if (row.ReviewStatus == ReviewStatus.NeedsReview)
                {
                    row.ReviewStatus = ReviewStatus.Confirmed;
                    row.UpdatedAt = time.GetUtcNow();
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);

                var setAside = await SetAsideOfAsync(database, settings, [row], cancellationToken);

                return Results.Ok(ToResponse(row, row.Client?.Name, IsBeforeRegistration(row, settings), setAside(row)));
            })
            .Produces<TransactionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        // Every year at once: an import backfill can leave rows waiting in a year the owner is not viewing.
        transactions.MapGet("/review", async (
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

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);
                var rows = await database.Transactions
                    .Include(row => row.Client)
                    .Include(row => row.RefundsTransaction)
                    .Include(row => row.BankAccount)
                    .Where(row => row.UserId == user.Id && row.ReviewStatus == ReviewStatus.NeedsReview)
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.BankTime)
                    .ThenByDescending(row => row.CreatedAt)
                    .ThenBy(row => row.Id)
                    .ToListAsync(cancellationToken);

                var setAside = await SetAsideOfAsync(database, settings, rows, cancellationToken);

                return Results.Ok(rows
                    .Select(row => ToResponse(row, row.Client?.Name, IsBeforeRegistration(row, settings), setAside(row)))
                    .ToArray());
            })
            .Produces<TransactionResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

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
                    .ThenBy(row => row.Id)
                    .Select(row => new ReceiptOption(
                        row.Id,
                        row.ValueDate,
                        row.AmountMinor,
                        row.Currency,
                        row.Client == null ? null : row.Client.Name,
                        row.ClientId))
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(receipts);
            })
            .Produces<ReceiptOption[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    // Asks the same Rule 8 decision `IncomeLedger.ForYear` makes, so a row's flag and the list's total
    // cannot disagree.
    internal static bool IsBeforeRegistration(Transaction row, SettingsEntity settings) =>
        settings.FopRegistrationDate is { } registrationDate
        && IncomeLedger.Exclusion(row.ToEngineInput(), registrationDate) is not null;

    // Rule 13: the rates are the row's year's, read from the stored year parameters. A year without a
    // row, or an owner without a registration date, has no rate to apply, so the row shows none.
    private static async Task<Func<Transaction, SetAsideResponse?>> SetAsideOfAsync(
        AppDbContext database,
        SettingsEntity settings,
        IReadOnlyCollection<Transaction> rows,
        CancellationToken cancellationToken)
    {
        if (settings.ToEngineInput().Group3Start is not { } group3Start || rows.Count == 0)
        {
            return _ => null;
        }

        var years = rows.Select(row => row.ValueDate.Year).Distinct().ToArray();
        var configs = await database.TaxYearConfigs
            .Where(config => years.Contains(config.Year))
            .ToDictionaryAsync(config => config.Year, config => config.ToEngineInput(), cancellationToken);

        return row => configs.TryGetValue(row.ValueDate.Year, out var config)
            && TaxReserve.SetAsideFor(row.ToEngineInput(), config, group3Start) is { } setAside
                ? new SetAsideResponse(setAside.SingleTaxKop, setAside.MilitaryLevyKop)
                : null;
    }

    private static TransactionResponse ToResponse(
        Transaction row, string? clientName, bool beforeRegistration, SetAsideResponse? setAside) =>
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
            row.ClientId,
            row.InvoiceId,
            row.InvoiceNumber,
            row.Description,
            beforeRegistration,
            row.RefundsTransaction is { } receipt
                ? new RefundedReceipt(receipt.Id, receipt.ValueDate, receipt.AmountMinor, receipt.Currency)
                : null,
            row.BankAccount is { } account
                ? new TransactionSource(account.Bank, IsoCurrency.Display(account.CurrencyCode))
                : null,
            row.ReviewStatus,
            setAside);
}
