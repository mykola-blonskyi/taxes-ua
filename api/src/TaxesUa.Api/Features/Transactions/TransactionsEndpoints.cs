using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Transactions;

public static class TransactionsEndpoints
{
    private const int MinYear = 2000;

    private const int MaxYear = 2100;

    // Caps the hryvnia result as well as the amount. Money.ToUahKop multiplies AmountMinor by RateE4
    // unchecked, and a result of at most 1e14 kop bounds that product by 1e14 x 10^4, inside long. 1e14
    // also stays inside JS Number.MAX_SAFE_INTEGER, which is what the web reads both as.
    private const long MaxAmountMinor = 100_000_000_000_000;

    // 1000.0000 UAH per unit.
    private const int MaxRateE4 = 10_000_000;

    private const int MaxReasonLength = 1000;

    private const int MaxClientNameLength = 200;

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

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);

                var rows = await database.Transactions
                    .Include(row => row.Client)
                    .Where(row => row.UserId == user.Id
                        && row.ValueDate >= new DateOnly(year, 1, 1)
                        && row.ValueDate < new DateOnly(year + 1, 1, 1))
                    .OrderByDescending(row => row.ValueDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .ToListAsync(cancellationToken);

                var (totalIncomeKop, beforeRegistration) = RunLedger(year, rows, settings);
                var items = rows
                    .Select(row => ToResponse(row, row.Client?.Name, beforeRegistration.Contains(row.ValueDate)))
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
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = Normalize(request);
                if (Validate(request, normalized) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var row = new Transaction { Id = Guid.NewGuid(), UserId = user.Id };
                if (await ApplyAmountAsync(row, request, rates, cancellationToken) is { } rateProblem)
                {
                    return rateProblem;
                }

                var now = DateTimeOffset.UtcNow;
                row.ClientId = await ResolveClientAsync(database, user.Id, normalized.ClientName, cancellationToken);
                row.Kind = request.Kind;
                row.NonIncomeReason = normalized.NonIncomeReason;
                row.InvoiceNumber = normalized.InvoiceNumber;
                row.Description = normalized.Description;
                row.CreatedAt = now;
                row.UpdatedAt = now;
                database.Transactions.Add(row);
                await database.SaveChangesAsync(cancellationToken);

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);
                var beforeRegistration = IsBeforeRegistration(row, settings);

                return Results.Created(
                    $"/api/transactions/{row.Id}",
                    ToResponse(row, normalized.ClientName, beforeRegistration));
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
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = Normalize(request);
                if (Validate(request, normalized) is { } errors)
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

                if (await ApplyAmountAsync(row, request, rates, cancellationToken) is { } rateProblem)
                {
                    return rateProblem;
                }

                row.ClientId = await ResolveClientAsync(database, user.Id, normalized.ClientName, cancellationToken);
                row.Kind = request.Kind;
                row.NonIncomeReason = normalized.NonIncomeReason;
                row.InvoiceNumber = normalized.InvoiceNumber;
                row.Description = normalized.Description;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);
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

                database.Transactions.Remove(row);
                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    // The absent row answers with the defaults, as GET /api/settings does, so an owner who never saved
    // settings is treated as having no registration date rather than as an error.
    private static async Task<SettingsEntity> LoadSettingsAsync(
        AppDbContext database, string userId, CancellationToken cancellationToken) =>
        await database.Settings.FindAsync([userId], cancellationToken) ?? new SettingsEntity { UserId = userId };

    private static IResult Missing(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No transaction exists with id {id}.");

    // The one place a stored row's engine input is compiled and run, so the list, the create response
    // and the update response can never diverge on Rule 1/Rule 8 (`IncomeLedger` owns both). The list
    // needs the year's total on top of the flagged dates the other two callers use alone.
    private static (long TotalIncomeKop, HashSet<DateOnly> FlaggedDates) RunLedger(
        int year, IReadOnlyList<Transaction> rows, SettingsEntity settings)
    {
        var income = IncomeLedger.ForYear(
            year, rows.Select(row => row.ToEngineInput()).ToList(), settings.ToEngineInput());
        var flagged = income.Warnings
            .OfType<EngineWarning.OperationBeforeRegistration>()
            .Select(warning => warning.ValueDate)
            .ToHashSet();

        return (income.TotalIncomeKop, flagged);
    }

    private static bool IsBeforeRegistration(Transaction row, SettingsEntity settings) =>
        RunLedger(row.ValueDate.Year, [row], settings).FlaggedDates.Contains(row.ValueDate);

    // The one place POST and PUT fix the rate (Rule 2), so the two cannot diverge. It runs before any
    // other change to the row or the context, since FxRates saves its cache row with its own
    // SaveChanges. On PUT, row still holds the stored values it compares against.
    private static async Task<IResult?> ApplyAmountAsync(
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
                return FxEndpoints.RateUnavailable(request.Currency, request.ValueDate, lookup);
            }

            rate = (found.RateE4, found.RateDate, RateSource.Nbu);
        }

        if ((Int128)request.AmountMinor * rate.RateE4 > (Int128)MaxAmountMinor * Money.RateScale)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
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

    private static async Task<Guid?> ResolveClientAsync(
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

    private static NormalizedText Normalize(TransactionRequest request) => new(
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
            beforeRegistration);

    private static Dictionary<string, string[]> YearOutOfRange() => new()
    {
        ["year"] = [$"year must be between {MinYear} and {MaxYear}."],
    };

    private static Dictionary<string, string[]>? Validate(TransactionRequest request, NormalizedText normalized)
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

        if (normalized.ClientName is { Length: > MaxClientNameLength })
        {
            errors[Field(nameof(request.ClientName))] =
                [$"clientName must not exceed {MaxClientNameLength} characters."];
        }

        if (normalized.InvoiceNumber is { Length: > MaxInvoiceNumberLength })
        {
            errors[Field(nameof(request.InvoiceNumber))] =
                [$"invoiceNumber must not exceed {MaxInvoiceNumberLength} characters."];
        }

        if (normalized.Description is { Length: > MaxDescriptionLength })
        {
            errors[Field(nameof(request.Description))] =
                [$"description must not exceed {MaxDescriptionLength} characters."];
        }

        return errors.Count == 0 ? null : errors;
    }

    // Derived rather than spelled a second time, so the key the web reads an error under cannot drift
    // from the member it is about. CamelCase is the policy JsonSerializerDefaults.Web applies to the
    // same member.
    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private readonly record struct NormalizedText(
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
    string? Description);

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
    bool BeforeRegistration);

internal sealed record TransactionListResponse(
    int Year,
    DateOnly? FopRegistrationDate,
    long TotalIncomeKop,
    TransactionResponse[] Items);
