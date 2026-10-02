namespace TaxesUa.Api.Features.Fx;

public static class FxEndpoints
{
    private const int MinYear = 2000;

    private const int MaxYear = 2100;

    public static IEndpointRouteBuilder MapFxApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/fx")
            .WithTags("Fx")
            .RequireAuthorization()
            .MapGet("", async (
                Currency currency,
                DateOnly date,
                FxRates rates,
                CancellationToken cancellationToken) =>
            {
                // The binder also accepts a number, so `currency=7` arrives as an undefined member.
                if (currency is not (Currency.USD or Currency.EUR))
                {
                    return Problems.Validation(nameof(currency), ProblemCodes.InvalidValue, "currency must be USD or EUR.");
                }

                if (date.Year < MinYear || date.Year > MaxYear)
                {
                    return Problems.Validation(
                        nameof(date),
                        ProblemCodes.YearOutOfRange,
                        $"date year must be between {MinYear} and {MaxYear}.");
                }

                var lookup = await rates.GetAsync(currency, date, cancellationToken);
                return lookup is NbuLookup.Found found
                    ? Results.Ok(new FxRateResponse(currency, date, found.RateE4, found.RateDate))
                    : RateUnavailable(currency, date, lookup);
            })
            .Produces<FxRateResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status502BadGateway);

        return routes;
    }

    // Shared with the transactions endpoints, so the web reads one 502 that tells the owner to enter
    // the rate manually wherever the rate was needed.
    internal static IResult RateUnavailable(Currency currency, DateOnly date, NbuLookup lookup) =>
        lookup is NbuLookup.NoRate
            ? Problems.Create(
                StatusCodes.Status502BadGateway,
                ProblemCodes.FxRateNotPublished,
                $"NBU published no {currency} rate for the {NbuRateClient.MaxDaysBack} days up to {date:yyyy-MM-dd}. "
                    + "Enter the rate manually.")
            : Problems.Create(
                StatusCodes.Status502BadGateway,
                ProblemCodes.FxServiceUnavailable,
                "The NBU rate service is unavailable. Enter the rate manually.");
}

internal sealed record FxRateResponse(Currency Currency, DateOnly Date, int RateE4, DateOnly RateDate);
