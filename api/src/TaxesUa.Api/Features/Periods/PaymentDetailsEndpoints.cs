using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Periods;

public static class PaymentDetailsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentDetailsApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/payment-details", async (
                PaymentKind kind,
                int periodYear,
                int? periodQuarter,
                int? periodMonth,
                long? amountKop,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (Validate(kind, periodYear, periodQuarter, periodMonth, amountKop) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var quarterOfPeriod = periodQuarter ?? (periodMonth!.Value + 2) / 3;
                var (viewed, notOffered) = await LoadOfferedAsync(database, user.Id, kind, periodYear, quarterOfPeriod, cancellationToken);
                if (notOffered is { } reason)
                {
                    return Problems.Create(StatusCodes.Status409Conflict, reason.Code, reason.Message);
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

                // The panel is used to pay now, so an account is judged on today: past its end it is withheld
                // (no details, no copy buttons, no QR); still valid but ending before the due date it is shown
                // with a warning (Rule 16).
                var expiry = row is not null && TreasuryAccountsEndpoints.ValidUntilOf(row) is { } validUntil && missing.Length == 0
                    ? ExpiryOf(viewed!, kind, periodYear, quarterOfPeriod, validUntil, time.TodayInKyiv())
                    : null;
                if (expiry is { State: PaymentAccountExpiryState.Expired })
                {
                    return Results.Ok(new PaymentDetailsResponse(
                        kind, periodYear, periodQuarter, periodMonth, amountKop, purpose, null, [], null, expiry));
                }

                var complete = missing.Length == 0;
                return Results.Ok(new PaymentDetailsResponse(
                    kind,
                    periodYear,
                    periodQuarter,
                    periodMonth,
                    amountKop,
                    purpose,
                    complete ? new PaymentRecipientResponse(iban!, name!, code!, source) : null,
                    missing,
                    complete && amountKop is { } amount ? NbuQr.Content(name!, iban!, code!, amount, purpose) : null,
                    expiry));
            })
            .WithTags("Payments")
            .RequireAuthorization()
            .Produces<PaymentDetailsResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<(YearAccruals? Viewed, Issue? NotOffered)> LoadOfferedAsync(
        AppDbContext database, string userId, PaymentKind kind, int year, int quarter, CancellationToken cancellationToken)
    {
        var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
        if (loaded is null)
        {
            return (null, new Issue(
                ProblemCodes.TaxYearNotConfigured,
                $"There is no tax configuration for {year}, so the app does not offer to pay its periods."));
        }

        return loaded.Viewed.Accrual.Accrues(kind, quarter)
            ? (loaded.Viewed, null)
            : (null, new Issue(
                ProblemCodes.PeriodNotPayable,
                $"Quarter {quarter} of {year} has no {kind} accrual outside group 3, so the app does not offer to pay it."));
    }

    private static PaymentAccountExpiryResponse? ExpiryOf(
        YearAccruals viewed, PaymentKind kind, int year, int quarter, DateOnly validUntil, DateOnly today)
    {
        if (today > validUntil)
        {
            return new PaymentAccountExpiryResponse(validUntil, PaymentAccountExpiryState.Expired);
        }

        // A month counts as its quarter (Rule 16).
        var deadlines = DeadlineCalendar.ForQuarter(year, quarter, viewed.Config.ToEngineInput(), viewed.Settings.ToEngineInput());
        var due = kind == PaymentKind.Esv ? deadlines.Esv.Due : deadlines.TaxPayment.Due;

        return due > validUntil ? new PaymentAccountExpiryResponse(validUntil, PaymentAccountExpiryState.ExpiresBeforeDue) : null;
    }

    private static FieldErrors? Validate(
        PaymentKind kind, int periodYear, int? periodQuarter, int? periodMonth, long? amountKop)
    {
        var errors = new FieldErrors();

        if (!Enum.IsDefined(kind))
        {
            errors.Set("kind", ProblemCodes.InvalidValue, "kind must be SingleTax, MilitaryLevy or Esv.");
        }

        if (amountKop <= 0)
        {
            errors.Set("amountKop", ProblemCodes.NotPositive, "amountKop must be positive.");
        }
        else if (amountKop > PaymentsEndpoints.MaxAmountKop)
        {
            errors.Set(
                "amountKop",
                ProblemCodes.AmountTooLarge,
                $"amountKop must not exceed {PaymentsEndpoints.MaxAmountKop}.");
        }

        if (!PaymentsEndpoints.InYearRange(periodYear))
        {
            errors.Set("periodYear", ProblemCodes.YearOutOfRange, PaymentsEndpoints.YearRangeMessage("periodYear"));
        }

        switch ((periodQuarter, periodMonth))
        {
            case (null, null):
            case (not null, not null):
                errors.Set(
                    "periodQuarter",
                    ProblemCodes.PeriodAmbiguous,
                    "Exactly one of periodQuarter and periodMonth must be set.");
                break;
            case (< 1 or > 4, _):
                errors.Set("periodQuarter", ProblemCodes.QuarterOutOfRange, "periodQuarter must be between 1 and 4.");
                break;
            case (_, < 1 or > 12):
                errors.Set("periodMonth", ProblemCodes.MonthOutOfRange, "periodMonth must be between 1 and 12.");
                break;
        }

        return errors.OrNull();
    }
}

internal sealed record PaymentDetailsResponse(
    PaymentKind Kind,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    long? AmountKop,
    string Purpose,
    PaymentRecipientResponse? Recipient,
    string[] Missing,
    string? QrContent,
    PaymentAccountExpiryResponse? Expiry);

/// <summary>
/// The account in use ends on <c>ValidUntil</c>. <c>Expired</c>: today is after it, so the response carries no
/// recipient and no QR. <c>ExpiresBeforeDue</c>: it is still valid today but ends before the period's due date.
/// </summary>
internal sealed record PaymentAccountExpiryResponse(DateOnly ValidUntil, PaymentAccountExpiryState State);

internal enum PaymentAccountExpiryState
{
    ExpiresBeforeDue,
    Expired,
}

internal sealed record PaymentRecipientResponse(string Iban, string Name, string Code, TreasuryAccountSource Source);
