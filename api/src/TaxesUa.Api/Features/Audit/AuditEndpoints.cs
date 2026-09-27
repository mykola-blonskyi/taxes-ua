using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Audit;

public static class AuditEndpoints
{
    // The newest entries only. One owner's log stays small enough that paging has not earned its place.
    private const int MaxEntries = 200;

    public static IEndpointRouteBuilder MapAuditApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/audit", async (
                AuditedEntity? entity,
                string? id,
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

                var query = database.AuditLog.AsNoTracking().Where(entry => entry.UserId == user.Id);
                if (entity is not null)
                {
                    query = query.Where(entry => entry.Entity == entity);
                }

                if (id is not null)
                {
                    query = query.Where(entry => entry.EntityId == id);
                }

                var entries = await query
                    .OrderByDescending(entry => entry.At)
                    .ThenByDescending(entry => entry.Id)
                    .Take(MaxEntries)
                    .ToListAsync(cancellationToken);

                return Results.Ok(entries.Select(ToResponse).ToArray());
            })
            .WithTags("Audit")
            .RequireAuthorization()
            .Produces<AuditEntryResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry) => new(
        entry.Id,
        entry.At,
        entry.Entity,
        entry.EntityId,
        entry.Action,
        Parse(entry.Before),
        Parse(entry.After));

    private static Dictionary<string, JsonElement>? Parse(string? snapshot) =>
        snapshot is null ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(snapshot);
}

internal sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset At,
    AuditedEntity Entity,
    string EntityId,
    AuditAction Action,
    Dictionary<string, JsonElement>? Before,
    Dictionary<string, JsonElement>? After);
