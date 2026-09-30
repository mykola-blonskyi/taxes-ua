using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

public static class PaymentDetailsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentDetailsApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/payment-details", async (
                PaymentKind kind,
                int periodYear,
                int? periodQuarter,
                int? periodMonth,
                long amountKop,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(kind, periodYear, periodQuarter, periodMonth, amountKop) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var quarterOfPeriod = periodQuarter ?? (periodMonth!.Value + 2) / 3;
                if (await NotOfferedAsync(database, user.Id, periodYear, quarterOfPeriod, cancellationToken) is { } reason)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: reason);
                }

                var row = await database.TreasuryAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(account => account.UserId == user.Id && account.Kind == kind, cancellationToken);
                var (source, iban, name, code) = row is null
                    ? (TreasuryAccountSource.None, null, null, null)
                    : TreasuryAccountsEndpoints.InUse(row);
                string[] missing =
                [
                    .. new[]
                    {
                        iban is null ? "iban" : null,
                        name is null ? "recipientName" : null,
                        code is null ? "recipientCode" : null,
                    }.OfType<string>(),
                ];
                var purpose = periodQuarter is { } quarter
                    ? PaymentPurpose.ForQuarter(kind, periodYear, quarter)
                    : PaymentPurpose.ForMonth(kind, periodYear, periodMonth!.Value);

                return Results.Ok(new PaymentDetailsResponse(
                    kind,
                    periodYear,
                    periodQuarter,
                    periodMonth,
                    amountKop,
                    purpose,
                    missing.Length == 0 ? new PaymentRecipientResponse(iban!, name!, code!, source) : null,
                    missing));
            })
            .WithTags("Payments")
            .RequireAuthorization()
            .Produces<PaymentDetailsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<string?> NotOfferedAsync(
        AppDbContext database, string userId, int year, int quarter, CancellationToken cancellationToken)
    {
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (loaded is null)
        {
            return $"There is no tax configuration for {year}, so the app does not offer to pay its periods.";
        }

        return loaded.Viewed.Accrual.InGroup3(quarter)
            ? null
            : $"Quarter {quarter} of {year} is outside group 3, so the app does not offer to pay it.";
    }

    private static Dictionary<string, string[]>? Validate(
        PaymentKind kind, int periodYear, int? periodQuarter, int? periodMonth, long amountKop)
    {
        var errors = new Dictionary<string, string[]>();

        if (!Enum.IsDefined(kind))
        {
            errors["kind"] = ["kind must be SingleTax, MilitaryLevy or Esv."];
        }

        if (amountKop <= 0)
        {
            errors["amountKop"] = ["amountKop must be positive."];
        }
        else if (amountKop > PaymentsEndpoints.MaxAmountKop)
        {
            errors["amountKop"] = [$"amountKop must not exceed {PaymentsEndpoints.MaxAmountKop}."];
        }

        if (!PaymentsEndpoints.InYearRange(periodYear))
        {
            errors["periodYear"] = [PaymentsEndpoints.YearRangeMessage("periodYear")];
        }

        switch ((periodQuarter, periodMonth))
        {
            case (null, null):
            case (not null, not null):
                errors["periodQuarter"] = ["Exactly one of periodQuarter and periodMonth must be set."];
                break;
            case (< 1 or > 4, _):
                errors["periodQuarter"] = ["periodQuarter must be between 1 and 4."];
                break;
            case (_, < 1 or > 12):
                errors["periodMonth"] = ["periodMonth must be between 1 and 12."];
                break;
        }

        return errors.Count == 0 ? null : errors;
    }
}

internal sealed record PaymentDetailsResponse(
    PaymentKind Kind,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    long AmountKop,
    string Purpose,
    PaymentRecipientResponse? Recipient,
    string[] Missing);

internal sealed record PaymentRecipientResponse(string Iban, string Name, string Code, TreasuryAccountSource Source);
