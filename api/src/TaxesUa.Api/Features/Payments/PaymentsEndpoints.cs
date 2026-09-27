using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Payments;

public static class PaymentsEndpoints
{
    // The same bound TransactionsEndpoints puts on an amount, and for the same reason: it stays inside
    // JS Number.MAX_SAFE_INTEGER, which is what the web reads a kopeck figure as.
    private const long MaxAmountKop = 100_000_000_000_000;

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

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);

                return Results.Ok(new PaymentListResponse(
                    year, [.. items.Select(row => ToResponse(row, IsBeforeRegistration(row, settings)))]));
            })
            .Produces<PaymentListResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        payments.MapPost("", async (
                PaymentRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request) is { } errors)
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

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);

                return Results.Created($"/api/payments/{row.Id}", ToResponse(row, IsBeforeRegistration(row, settings)));
            })
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        payments.MapPut("/{id:guid}", async (
                Guid id,
                PaymentRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var row = await database.BudgetPayments
                    .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id, cancellationToken);
                if (row is null)
                {
                    return Missing(id);
                }

                Apply(row, request, DateTimeOffset.UtcNow);
                await database.SaveChangesAsync(cancellationToken);

                var settings = await LoadSettingsAsync(database, user.Id, cancellationToken);

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

                var deleted = await database.BudgetPayments
                    .Where(p => p.Id == id && p.UserId == user.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                return deleted == 0 ? Missing(id) : Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    /// <summary>
    /// The owner's payments recorded against <paramref name="year"/>, as the engine reads them. A
    /// payment belongs to the year of its period and not of <c>PaidOn</c>, so a Q4 payment made the
    /// next February still settles Q4.
    /// </summary>
    internal static async Task<IReadOnlyList<BudgetPaymentInput>> LoadEngineInputAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var rows = await database.BudgetPayments
            .AsNoTracking()
            .Where(row => row.UserId == userId && row.PeriodYear == year)
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

    // The absent row answers with the defaults, as GET /api/settings does, so an owner who never saved
    // settings is treated as having no registration date rather than as an error.
    private static async Task<SettingsEntity> LoadSettingsAsync(
        AppDbContext database, string userId, CancellationToken cancellationToken) =>
        await database.Settings.FindAsync([userId], cancellationToken) ?? new SettingsEntity { UserId = userId };

    // A soft warning only (Rule 8 does not govern payments): the payment is still saved and credited,
    // the owner just gets flagged to double-check the date.
    private static bool IsBeforeRegistration(BudgetPayment row, SettingsEntity settings) =>
        settings.FopRegistrationDate is { } registrationDate && row.PaidOn < registrationDate;

    private static IResult Missing(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No payment exists with id {id}.");

    internal static Dictionary<string, string[]>? Validate(PaymentRequest request)
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

        return errors.Count == 0 ? null : errors;
    }

    private static bool InYearRange(int year) =>
        year >= TransactionsEndpoints.MinYear && year <= TransactionsEndpoints.MaxYear;

    private static string YearRangeMessage(string subject) =>
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
