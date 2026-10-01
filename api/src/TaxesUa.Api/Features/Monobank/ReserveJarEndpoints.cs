using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Monobank;

public static class ReserveJarEndpoints
{
    public static IEndpointRouteBuilder MapReserveJarApi(this IEndpointRouteBuilder routes)
    {
        var monobank = routes.MapGroup("/monobank").WithTags("Monobank").RequireAuthorization();

        // The stored choice, read without asking the bank.
        monobank.MapGet("/reserve-jar", async (
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

                var row = await database.ReserveJars.AsNoTracking()
                    .FirstOrDefaultAsync(jar => jar.UserId == user.Id, cancellationToken);
                return Results.Ok(new ReserveJarStateResponse(row is null ? null : ReserveJarResponse.Of(row, time.GetUtcNow())));
            })
            .Produces<ReserveJarStateResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // The owner's UAH jars as the bank reports them now, through the rate gate.
        monobank.MapGet("/jars", async (
                UserManager<ApplicationUser> users,
                ReserveJarService jars,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await jars.ListAsync(user.Id, cancellationToken) switch
                {
                    JarOutcome.Listed listed => Results.Ok(new JarChoicesResponse(
                        [.. listed.Jars.Select(jar => new JarChoiceResponse(jar.Id, jar.Title, jar.BalanceKop))], listed.At)),
                    var failed => Failure(http, failed),
                };
            })
            .Produces<JarChoicesResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        monobank.MapPut("/reserve-jar", async (
                ChooseReserveJarRequest request,
                UserManager<ApplicationUser> users,
                ReserveJarService jars,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.JarId))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["jarId"] = ["jarId is required."],
                    });
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await jars.ChooseAsync(user.Id, request.JarId, cancellationToken) switch
                {
                    JarOutcome.Stored stored => Results.Ok(ReserveJarResponse.Of(stored.Jar, time.GetUtcNow())),
                    JarOutcome.JarNotOffered => Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["jarId"] = ["jarId is not one of your UAH jars."],
                    }),
                    var failed => Failure(http, failed),
                };
            })
            .Produces<ReserveJarResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        monobank.MapDelete("/reserve-jar", async (
                UserManager<ApplicationUser> users,
                ReserveJarService jars,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                await jars.ClearAsync(user.Id, cancellationToken);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        // Reads the bank through the gate. When the slot is taken it answers 429 with the seconds to wait
        // rather than parking the request, and an answer under a minute old is reused instead.
        monobank.MapPost("/reserve-jar/refresh", async (
                UserManager<ApplicationUser> users,
                ReserveJarService jars,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await jars.RefreshAsync(user.Id, cancellationToken) switch
                {
                    JarOutcome.Stored stored => Results.Ok(ReserveJarResponse.Of(stored.Jar, time.GetUtcNow())),
                    var failed => Failure(http, failed),
                };
            })
            .Produces<ReserveJarResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return routes;
    }

    private static IResult Failure(HttpContext http, JarOutcome outcome)
    {
        switch (outcome)
        {
            case JarOutcome.Waiting waiting:
                // Whole seconds, rounded up, so a client that waits that long finds the slot free.
                var seconds = (int)Math.Ceiling(waiting.RetryAfter.TotalSeconds);
                http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Problem(
                    title: "monobank allows one request a minute.",
                    detail: $"Try again in {seconds} seconds.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            case JarOutcome.NotConnected:
                return Results.Problem(title: "monobank is not connected.", statusCode: StatusCodes.Status409Conflict);
            case JarOutcome.NoJarChosen:
                return Results.Problem(title: "No reserve jar is chosen.", statusCode: StatusCodes.Status409Conflict);
            case JarOutcome.JarNotOffered:
                return Results.Problem(
                    title: "The reserve jar is no longer among your UAH jars.", statusCode: StatusCodes.Status409Conflict);
            case JarOutcome.InvalidToken:
                return Results.Problem(
                    title: "monobank rejected the token; replace it first.", statusCode: StatusCodes.Status409Conflict);
            case JarOutcome.Unavailable unavailable:
                return Results.Problem(
                    title: "monobank is temporarily unavailable.",
                    detail: unavailable.Reason,
                    statusCode: StatusCodes.Status502BadGateway);
            default:
                throw new InvalidOperationException($"Unhandled {nameof(JarOutcome)}.");
        }
    }
}

internal sealed record ChooseReserveJarRequest(string JarId);

/// <summary>
/// The owner's chosen jar. <c>Stale</c> is true once <c>FetchedAt</c> is more than a day old, which means
/// refreshes have been failing; the screen shows the time either way.
/// </summary>
internal sealed record ReserveJarResponse(string JarId, string Title, long BalanceKop, DateTimeOffset FetchedAt, bool Stale)
{
    public static ReserveJarResponse Of(ReserveJar row, DateTimeOffset now) =>
        new(row.JarId, row.Title, row.BalanceKop, row.FetchedAt, ReserveJarService.IsStale(row.FetchedAt, now));
}

internal sealed record ReserveJarStateResponse(ReserveJarResponse? Jar);

internal sealed record JarChoiceResponse(string Id, string Title, long BalanceKop);

internal sealed record JarChoicesResponse(JarChoiceResponse[] Jars, DateTimeOffset ReadAt);
