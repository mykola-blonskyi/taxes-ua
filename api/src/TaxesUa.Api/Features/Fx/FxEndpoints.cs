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
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(currency)] = ["currency must be USD or EUR."],
                    });
                }

                if (date.Year < MinYear || date.Year > MaxYear)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(date)] = [$"date year must be between {MinYear} and {MaxYear}."],
                    });
                }

                var lookup = await rates.GetAsync(currency, date, cancellationToken);
                return lookup is NbuLookup.Found found
                    ? Results.Ok(new FxRateResponse(currency, date, found.RateE4, found.RateDate))
                    : RateUnavailable(currency, date, lookup);
            })
            .Produces<FxRateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return routes;
    }

    // Shared with the transactions endpoints, so the web reads one 502 that tells the owner to enter
    // the rate manually wherever the rate was needed.
    internal static IResult RateUnavailable(Currency currency, DateOnly date, NbuLookup lookup) => Results.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: lookup is NbuLookup.NoRate
            ? $"NBU published no {currency} rate for the {NbuRateClient.MaxDaysBack} days up to {date:yyyy-MM-dd}. "
                + "Enter the rate manually."
            : "The NBU rate service is unavailable. Enter the rate manually.");
}

internal sealed record FxRateResponse(Currency Currency, DateOnly Date, int RateE4, DateOnly RateDate);
