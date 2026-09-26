using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.TaxYears;

public static class TaxYearEndpoints
{
    private const int MinYear = 2000;

    private const int MaxYear = 2100;

    // The engine lands these two days in a month it derives: the month after a quarter for the ESV
    // deadline, which is April at the earliest, and the month after any month for an advance, which
    // can be February. 28 is the one bound that names a day every month has, so neither day needs a
    // per-month special case and neither can reach the engine as an impossible date.
    private const int LastDayEveryMonthHas = 28;

    private const int MaxRateBp = 10_000;

    private const int MaxDaysInYear = 366;

    private const int MaxThresholdPct = 1_000;

    private const long Unbounded = long.MaxValue;

    public static IEndpointRouteBuilder MapTaxYearsApi(this IEndpointRouteBuilder routes)
    {
        var taxYears = routes.MapGroup("/tax-years").WithTags("TaxYears").RequireAuthorization();

        taxYears.MapGet("", async (AppDbContext database, CancellationToken cancellationToken) =>
            {
                var configs = await database.TaxYearConfigs
                    .AsNoTracking()
                    .OrderBy(config => config.Year)
                    .ToListAsync(cancellationToken);

                return Results.Ok(configs.Select(ToResponse).ToArray());
            })
            .Produces<TaxYearConfigResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        taxYears.MapGet("/{year:int}", async (int year, AppDbContext database, CancellationToken cancellationToken) =>
            {
                var config = await database.TaxYearConfigs.FindAsync([year], cancellationToken);

                return config is null ? Missing(year) : Results.Ok(ToResponse(config));
            })
            .Produces<TaxYearConfigResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        taxYears.MapPut("/{year:int}", async (
                int year,
                TaxYearConfigRequest request,
                AppDbContext database,
                CancellationToken cancellationToken) =>
            {
                if (Validate(Bounds(year, request)) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var config = await database.TaxYearConfigs.FindAsync([year], cancellationToken);
                if (config is null)
                {
                    config = new TaxYearConfig { Year = year };
                    database.TaxYearConfigs.Add(config);
                }

                Apply(config, request);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(config));
            })
            .Produces<TaxYearConfigResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        taxYears.MapPost("/{year:int}/verify", async (
                int year,
                AppDbContext database,
                CancellationToken cancellationToken) =>
            {
                var config = await database.TaxYearConfigs.FindAsync([year], cancellationToken);
                if (config is null)
                {
                    return Missing(year);
                }

                config.VerifiedAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(config));
            })
            .Produces<TaxYearConfigResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        taxYears.MapPost("/{year:int}/clone-to/{next:int}", async (
                int year,
                int next,
                AppDbContext database,
                CancellationToken cancellationToken) =>
            {
                if (Validate([YearBound("Year", year), YearBound("Next", next)]) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var source = await database.TaxYearConfigs.FindAsync([year], cancellationToken);
                if (source is null)
                {
                    return Missing(year);
                }

                if (await database.TaxYearConfigs.AnyAsync(config => config.Year == next, cancellationToken))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"A tax year configuration already exists for {next}.");
                }

                var copy = source.CloneTo(next);
                database.TaxYearConfigs.Add(copy);
                await database.SaveChangesAsync(cancellationToken);

                return Results.Created($"/api/tax-years/{next}", ToResponse(copy));
            })
            .Produces<TaxYearConfigResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static IResult Missing(int year) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No tax year configuration exists for {year}.");

    // A write invalidates the verification, which attested to the numbers that were stored before it.
    private static void Apply(TaxYearConfig config, TaxYearConfigRequest request)
    {
        config.MinWageKop = request.MinWageKop;
        config.SingleTaxRateBp = request.SingleTaxRateBp;
        config.MilitaryLevyRateBp = request.MilitaryLevyRateBp;
        config.EsvRateBp = request.EsvRateBp;
        config.ExcessRateBp = request.ExcessRateBp;
        config.IncomeLimitMinWages = request.IncomeLimitMinWages;
        config.LimitWarnThresholdsPct = [.. request.LimitWarnThresholdsPct];
        config.EsvDeadlineDay = request.EsvDeadlineDay;
        config.DeclarationDays = request.DeclarationDays;
        config.TaxPaymentDaysAfterDeclaration = request.TaxPaymentDaysAfterDeclaration;
        config.AdvanceRecommendedDay = request.AdvanceRecommendedDay;
        config.Holidays = [.. request.Holidays];
        config.Source = request.Source;
        config.VerifiedAt = null;
        config.RecomputeDerived();
    }

    private static TaxYearConfigResponse ToResponse(TaxYearConfig config) => new(
        config.Year,
        config.MinWageKop,
        config.SingleTaxRateBp,
        config.MilitaryLevyRateBp,
        config.EsvRateBp,
        config.ExcessRateBp,
        config.EsvMonthlyKop,
        config.IncomeLimitMinWages,
        config.IncomeLimitKop,
        config.LimitWarnThresholdsPct,
        config.EsvDeadlineDay,
        config.DeclarationDays,
        config.TaxPaymentDaysAfterDeclaration,
        config.AdvanceRecommendedDay,
        config.Holidays,
        config.Source,
        config.VerifiedAt);

    private static IEnumerable<Bound> Bounds(int year, TaxYearConfigRequest request)
    {
        yield return YearBound(nameof(TaxYearConfigResponse.Year), year);
        yield return new(nameof(request.MinWageKop), request.MinWageKop, 1, Unbounded);
        yield return new(nameof(request.SingleTaxRateBp), request.SingleTaxRateBp, 0, MaxRateBp);
        yield return new(nameof(request.MilitaryLevyRateBp), request.MilitaryLevyRateBp, 0, MaxRateBp);
        yield return new(nameof(request.EsvRateBp), request.EsvRateBp, 0, MaxRateBp);
        yield return new(nameof(request.ExcessRateBp), request.ExcessRateBp, 0, MaxRateBp);
        yield return new(nameof(request.IncomeLimitMinWages), request.IncomeLimitMinWages, 1, Unbounded);
        yield return new(
            nameof(request.EsvDeadlineDay),
            request.EsvDeadlineDay,
            1,
            LastDayEveryMonthHas,
            "so the ESV deadline is a date the month after every quarter has");
        yield return new(nameof(request.DeclarationDays), request.DeclarationDays, 1, MaxDaysInYear);
        yield return new(
            nameof(request.TaxPaymentDaysAfterDeclaration),
            request.TaxPaymentDaysAfterDeclaration,
            0,
            MaxDaysInYear);
        yield return new(
            nameof(request.AdvanceRecommendedDay),
            request.AdvanceRecommendedDay,
            1,
            LastDayEveryMonthHas,
            "so the recommended advance date is a date every following month has");

        for (var index = 0; index < request.LimitWarnThresholdsPct.Length; index++)
        {
            yield return new(
                nameof(request.LimitWarnThresholdsPct),
                request.LimitWarnThresholdsPct[index],
                1,
                MaxThresholdPct,
                $"and element {index} is not");
        }
    }

    private static Bound YearBound(string name, int year) => new(name, year, MinYear, MaxYear);

    private static Dictionary<string, string[]>? Validate(IEnumerable<Bound> bounds)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var bound in bounds)
        {
            if (bound.Value >= bound.Min && bound.Value <= bound.Max)
            {
                continue;
            }

            if (!errors.TryGetValue(bound.Field, out var messages))
            {
                errors[bound.Field] = messages = [];
            }

            messages.Add(bound.Message);
        }

        return errors.Count == 0
            ? null
            : errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
    }

    private readonly record struct Bound(
        string Name,
        long Value,
        long Min,
        long Max,
        string Because = "")
    {
        // Derived rather than spelled twice, so the key the web reads an error under cannot drift
        // from the member it is about. It is the same policy JsonSerializerDefaults.Web applies.
        public string Field => JsonNamingPolicy.CamelCase.ConvertName(Name);

        public string Message
        {
            get
            {
                var range = Max == Unbounded ? $"at least {Min}" : $"between {Min} and {Max}";
                return Because.Length == 0
                    ? $"{Name} must be {range}."
                    : $"{Name} must be {range}, {Because}.";
            }
        }
    }
}

internal sealed record TaxYearConfigRequest(
    long MinWageKop,
    int SingleTaxRateBp,
    int MilitaryLevyRateBp,
    int EsvRateBp,
    int ExcessRateBp,
    int IncomeLimitMinWages,
    int[] LimitWarnThresholdsPct,
    int EsvDeadlineDay,
    int DeclarationDays,
    int TaxPaymentDaysAfterDeclaration,
    int AdvanceRecommendedDay,
    DateOnly[] Holidays,
    string Source);

internal sealed record TaxYearConfigResponse(
    int Year,
    long MinWageKop,
    int SingleTaxRateBp,
    int MilitaryLevyRateBp,
    int EsvRateBp,
    int ExcessRateBp,
    long EsvMonthlyKop,
    int IncomeLimitMinWages,
    long IncomeLimitKop,
    int[] LimitWarnThresholdsPct,
    int EsvDeadlineDay,
    int DeclarationDays,
    int TaxPaymentDaysAfterDeclaration,
    int AdvanceRecommendedDay,
    DateOnly[] Holidays,
    string Source,
    DateTimeOffset? VerifiedAt);
