using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Features.Calendar;

public static class CalendarEndpoints
{
    private const string FeedRoute = "/calendar/feed";

    public static IEndpointRouteBuilder MapCalendarApi(this IEndpointRouteBuilder routes)
    {
        var calendar = routes.MapGroup("/calendar").WithTags("Calendar").RequireAuthorization();

        calendar.MapGet("/feed", async (
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

                // Only the hash is stored, so a read can say that a link exists but never show it again.
                var createdAt = await database.CalendarFeeds
                    .Where(feed => feed.UserId == user.Id)
                    .Select(feed => (DateTimeOffset?)feed.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                return Results.Ok(new CalendarFeedResponse(createdAt));
            })
            .Produces<CalendarFeedResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // Draws a new secret whether or not the owner had one, so the old URL stops answering at once.
        calendar.MapPost("/feed/rotate", async (
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

                var feed = await database.CalendarFeeds.FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                if (feed is null)
                {
                    feed = new CalendarFeed { UserId = user.Id };
                    database.CalendarFeeds.Add(feed);
                }

                var secret = PathSecret.New();
                feed.SecretHash = PathSecret.Hash(secret);
                feed.CreatedAt = time.GetUtcNow();
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new CalendarFeedLinkResponse(PathOf(secret), feed.CreatedAt));
            })
            .Produces<CalendarFeedLinkResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        calendar.MapGet("/deadlines.ics", async (
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

                http.Response.Headers.CacheControl = "no-store";
                var document = await RenderAsync(database, time, user.Id, cancellationToken);
                return Results.File(Encoding.UTF8.GetBytes(document), IcsDocument.ContentType, "taxes-ua-deadlines.ics");
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/calendar")
            .Produces(StatusCodes.Status401Unauthorized);

        // The subscription itself (ADR-017): anonymous, found only by the secret in the path, left out of
        // the OpenAPI document, and 404 for any other value. Calendar apps send no cookie.
        routes.MapGet(FeedRoute + "/{secret}.ics", async (
                string secret, AppDbContext database, TimeProvider time, HttpContext http, CancellationToken cancellationToken) =>
            {
                var hash = PathSecret.Hash(secret);
                var ownerId = await database.CalendarFeeds
                    .Where(feed => feed.SecretHash == hash)
                    .Select(feed => feed.UserId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (ownerId is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "no-store";
                var document = await RenderAsync(database, time, ownerId, cancellationToken);
                return Results.Text(document, IcsDocument.ContentType);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        return routes;
    }

    private static string PathOf(string secret) => "/api" + FeedRoute + "/" + secret + ".ics";

    private static async Task<string> RenderAsync(
        AppDbContext database, TimeProvider time, string userId, CancellationToken cancellationToken)
    {
        var locale = TelegramTexts.LocaleOfLanguage(
            await database.Settings.Where(row => row.UserId == userId).Select(row => row.Locale).FirstOrDefaultAsync(cancellationToken));
        var deadlines = await CalendarDeadline.LoadAsync(database, userId, time.TodayInKyiv(), cancellationToken);
        return IcsDocument.Render(deadlines, locale, time.GetUtcNow());
    }
}

// CreatedAt is null while the owner has no link.
internal sealed record CalendarFeedResponse(DateTimeOffset? CreatedAt);

// The only answer that carries the URL: the one that draws its secret.
internal sealed record CalendarFeedLinkResponse(string Path, DateTimeOffset CreatedAt);
