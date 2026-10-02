using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Payments;

public static class PaymentsEndpoints
{
    // The same bound TransactionsEndpoints puts on an amount, and for the same reason: it stays inside
    // JS Number.MAX_SAFE_INTEGER, which is what the web reads a kopeck figure as.
    internal const long MaxAmountKop = 100_000_000_000_000;

    private const int MaxNoteLength = 1000;

    public static IEndpointRouteBuilder MapPaymentsApi(this IEndpointRouteBuilder routes)
    {
        var payments = routes.MapGroup("/payments").WithTags("Payments").RequireAuthorization();

        payments.MapGet("", async (
                int year,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (year < TransactionsEndpoints.MinYear || year > TransactionsEndpoints.MaxYear)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["year"] = [YearRangeMessage("year")],
                    });
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var items = await database.BudgetPayments
                    .Where(row => row.UserId == user.Id && row.PeriodYear == year)
                    .OrderByDescending(row => row.PaidOn)
                    .ThenByDescending(row => row.CreatedAt)
                    .ToListAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);

                return Results.Ok(new PaymentListResponse(
                    year, [.. items.Select(row => ToResponse(row, IsBeforeRegistration(row, settings)))]));
            })
            .Produces<PaymentListResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        payments.MapPost("", async (
                PaymentRequest request,
                TimeProvider time,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request, time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var now = DateTimeOffset.UtcNow;
                var row = new BudgetPayment { Id = Guid.NewGuid(), UserId = user.Id, CreatedAt = now };
                Apply(row, request, now);
                database.BudgetPayments.Add(row);
                await database.SaveChangesAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);

                return Results.Created($"/api/payments/{row.Id}", ToResponse(row, IsBeforeRegistration(row, settings)));
            })
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        payments.MapPut("/{id:guid}", async (
                Guid id,
                PaymentRequest request,
                TimeProvider time,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request, time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                // Under the owner's lock, as a confirm is, so a candidate is never read half-changed.
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await PaymentCandidatesEndpoints.LockOwnerAsync(database, user.Id, cancellationToken);
                var row = await database.BudgetPayments
                    .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Missing(id);
                }

                var now = DateTimeOffset.UtcNow;
                Apply(row, request, now);
                await PaymentCandidatesEndpoints.FollowPaymentAsync(database, row, request.Kind, now, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken);

                return Results.Ok(ToResponse(row, IsBeforeRegistration(row, settings)));
            })
            .Produces<PaymentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        payments.MapDelete("/{id:guid}", async (
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
                var row = await database.BudgetPayments
                    .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Missing(id);
                }

                database.BudgetPayments.Remove(row);
                await PaymentCandidatesEndpoints.FollowPaymentAsync(database, row, null, DateTimeOffset.UtcNow, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    /// <summary>
    /// The owner's payments named for any year from <paramref name="fromYear"/> through
    /// <paramref name="toYear"/>, as the engine reads them. A payment belongs to the year of its period
    /// and not of <c>PaidOn</c>; within its kind it settles the oldest debt first (Rule 7).
    /// </summary>
    internal static async Task<IReadOnlyList<BudgetPaymentInput>> LoadEngineInputAsync(
        AppDbContext database, string userId, int fromYear, int toYear, CancellationToken cancellationToken)
    {
        var rows = await database.BudgetPayments
            .AsNoTracking()
            .Where(row => row.UserId == userId && row.PeriodYear >= fromYear && row.PeriodYear <= toYear)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => row.ToEngineInput())];
    }

    internal static void Apply(BudgetPayment row, PaymentRequest request, DateTimeOffset now)
    {
        row.PaidOn = request.PaidOn;
        row.Kind = request.Kind;
        row.AmountKop = request.AmountKop;
        row.PeriodYear = request.PeriodYear;
        row.PeriodQuarter = request.PeriodQuarter;
        row.PeriodMonth = request.PeriodMonth;
        row.Note = Trim(request.Note);
        row.UpdatedAt = now;
    }

    internal static PaymentResponse ToResponse(BudgetPayment row, SettingsEntity settings) =>
        ToResponse(row, IsBeforeRegistration(row, settings));

    private static PaymentResponse ToResponse(BudgetPayment row, bool beforeRegistration) => new(
        row.Id,
        row.PaidOn,
        row.Kind,
        row.AmountKop,
        row.PeriodYear,
        row.PeriodQuarter,
        row.PeriodMonth,
        row.Note,
        beforeRegistration);

    // A soft warning only (Rule 8 does not govern payments): the payment is still saved and credited,
    // the owner just gets flagged to double-check the date.
    private static bool IsBeforeRegistration(BudgetPayment row, SettingsEntity settings) =>
        settings.FopRegistrationDate is { } registrationDate && row.PaidOn < registrationDate;

    private static IResult Missing(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No payment exists with id {id}.");

    /// <summary>
    /// A payment is money already paid, so when <paramref name="today"/> (Kyiv, Rule 10) is given its date
    /// may not lie after it. A date before the period is allowed, as an advance is paid ahead (Rule 6),
    /// and one before the FOP registration only raises <c>BeforeRegistration</c>. Backup import and the
    /// bank candidates pass no day: they carry dates that were true when they were written.
    /// </summary>
    internal static Dictionary<string, string[]>? Validate(PaymentRequest request, DateOnly? today = null)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.AmountKop <= 0)
        {
            errors[Field(nameof(request.AmountKop))] = ["amountKop must be positive."];
        }
        else if (request.AmountKop > MaxAmountKop)
        {
            errors[Field(nameof(request.AmountKop))] = [$"amountKop must not exceed {MaxAmountKop}."];
        }

        if (!InYearRange(request.PaidOn.Year))
        {
            errors[Field(nameof(request.PaidOn))] = [YearRangeMessage("paidOn year")];
        }
        else if (today is { } latest && request.PaidOn > latest)
        {
            errors[Field(nameof(request.PaidOn))] = [$"paidOn must not be after today ({latest:yyyy-MM-dd})."];
        }

        if (!InYearRange(request.PeriodYear))
        {
            errors[Field(nameof(request.PeriodYear))] = [YearRangeMessage("periodYear")];
        }

        switch (request)
        {
            case { PeriodQuarter: null, PeriodMonth: null }:
            case { PeriodQuarter: not null, PeriodMonth: not null }:
                errors[Field(nameof(request.PeriodQuarter))] =
                    ["Exactly one of periodQuarter and periodMonth must be set."];
                break;
            case { PeriodQuarter: < 1 or > 4 }:
                errors[Field(nameof(request.PeriodQuarter))] = ["periodQuarter must be between 1 and 4."];
                break;
            case { PeriodMonth: < 1 or > 12 }:
                errors[Field(nameof(request.PeriodMonth))] = ["periodMonth must be between 1 and 12."];
                break;
        }

        if (Trim(request.Note) is { Length: > MaxNoteLength })
        {
            errors[Field(nameof(request.Note))] = [$"note must not exceed {MaxNoteLength} characters."];
        }
        else if (Trim(request.Note) is { } note && TextRules.HasDisallowedControlChar(note))
        {
            errors[Field(nameof(request.Note))] =
                ["note must not contain a NUL or other control character (tab, line feed and carriage return are allowed)."];
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static bool InYearRange(int year) =>
        year >= TransactionsEndpoints.MinYear && year <= TransactionsEndpoints.MaxYear;

    internal static string YearRangeMessage(string subject) =>
        $"{subject} must be between {TransactionsEndpoints.MinYear} and {TransactionsEndpoints.MaxYear}.";

    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

internal sealed record PaymentRequest(
    DateOnly PaidOn,
    PaymentKind Kind,
    long AmountKop,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    string? Note);

internal sealed record PaymentResponse(
    Guid Id,
    DateOnly PaidOn,
    PaymentKind Kind,
    long AmountKop,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    string? Note,
    bool BeforeRegistration);

internal sealed record PaymentListResponse(int Year, PaymentResponse[] Items);
