using System.Text.Json;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.TaxYears;

public static class LimitationSuspensionEndpoints
{
    public static IEndpointRouteBuilder MapLimitationSuspensionApi(this IEndpointRouteBuilder routes)
    {
        var suspension = routes.MapGroup("/limitation-suspension").WithTags("TaxYears").RequireAuthorization();

        suspension.MapGet("", async (AppDbContext database, CancellationToken cancellationToken) =>
                Results.Ok(ToResponse(await LoadAsync(database, cancellationToken))))
            .Produces<LimitationSuspensionResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        suspension.MapPut("", async (
                LimitationSuspensionRequest request,
                AppDbContext database,
                CancellationToken cancellationToken) =>
            {
                if (Validate(request) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var config = await LoadAsync(database, cancellationToken);
                config.Start = request.Start;
                config.End = request.End;
                config.Source = request.Source;
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(config));
            })
            .AddEndpointFilter<AdminOnlyFilter>()
            .Produces<LimitationSuspensionResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status403Forbidden);

        return routes;
    }

    /// <summary>The seeded row; a database without it was not migrated, which is a deploy error.</summary>
    internal static async Task<LimitationSuspensionConfig> LoadAsync(
        AppDbContext database, CancellationToken cancellationToken) =>
        await database.LimitationSuspensionConfigs.FindAsync([LimitationSuspensionConfig.SingletonId], cancellationToken)
            ?? throw new InvalidOperationException("The limitation suspension row is missing; run the migrations.");

    private static FieldErrors? Validate(LimitationSuspensionRequest request)
    {
        var errors = new FieldErrors();
        if (request.Start.Year is < Limits.MinYear or > Limits.MaxYear)
        {
            errors.Set(Field(nameof(request.Start)), ProblemCodes.YearOutOfRange, Range("start"));
        }

        if (request.End is { } end)
        {
            if (end.Year is < Limits.MinYear or > Limits.MaxYear)
            {
                errors.Set(Field(nameof(request.End)), ProblemCodes.YearOutOfRange, Range("end"));
            }
            else if (end < request.Start)
            {
                errors.Set(
                    Field(nameof(request.End)),
                    ProblemCodes.SuspensionEndBeforeStart,
                    "end must not be before start.");
            }
        }

        if (TextRules.HasDisallowedControlChar(request.Source))
        {
            errors.Set(
                Field(nameof(request.Source)),
                ProblemCodes.ControlCharacter,
                "source must not contain a NUL or other control character "
                    + "(tab, line feed and carriage return are allowed).");
        }

        return errors.OrNull();
    }

    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string Range(string name) => $"{name} must fall in a year from {Limits.MinYear} to {Limits.MaxYear}.";

    private static LimitationSuspensionResponse ToResponse(LimitationSuspensionConfig config) =>
        new(config.Start, config.End, config.Source);
}

internal sealed record LimitationSuspensionRequest(DateOnly Start, DateOnly? End, string Source);

/// <summary><c>End</c> is the last suspended day, null while the suspension lasts.</summary>
internal sealed record LimitationSuspensionResponse(DateOnly Start, DateOnly? End, string Source);
