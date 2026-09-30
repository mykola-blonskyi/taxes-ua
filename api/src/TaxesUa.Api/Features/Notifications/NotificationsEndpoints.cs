using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Notifications;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsApi(this IEndpointRouteBuilder routes)
    {
        var notifications = routes.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("/channels", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new[] { await LoadTelegramAsync(database, bot, user.Id, cancellationToken) });
            })
            .Produces<NotificationChannelResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        // Answers the deep link the owner opens in Telegram. The bot's username comes from getMe, so
        // nothing about the bot is configured besides its token.
        notifications.MapPost("/channels/telegram/connect", async (
                UserManager<ApplicationUser> users,
                TelegramBot bot,
                TelegramClient client,
                TelegramLinking linking,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!bot.IsConfigured)
                {
                    return NotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var identity = await client.GetMeAsync(cancellationToken);
                if (!identity.IsOk)
                {
                    return Results.Problem(
                        title: "Telegram is temporarily unavailable.",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                var (code, expiresAt) = await linking.IssueAsync(user.Id, cancellationToken);

                return Results.Ok(new TelegramConnectResponse(
                    $"https://t.me/{identity.Value!.Username}?start={code}", expiresAt));
            })
            .Produces<TelegramConnectResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notifications.MapPut("/channels/telegram", async (
                TelegramToggleRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var channel = await FindAsync(database, user.Id, cancellationToken);
                if (channel is null)
                {
                    return Results.Problem(title: "Connect Telegram first.", statusCode: StatusCodes.Status409Conflict);
                }

                channel.Enabled = request.Enabled;
                if (request.Enabled)
                {
                    channel.LastFailure = null;
                    channel.LastFailureAt = null;
                }

                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(bot, channel));
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        notifications.MapPost("/channels/telegram/test", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                TelegramDelivery delivery,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!bot.IsConfigured)
                {
                    return NotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var locale = (await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken)).Locale;
                var result = await delivery.SendAsync(user.Id, TelegramTexts.Test(locale), evenIfDisabled: true, cancellationToken);
                switch (result.Outcome)
                {
                    case DeliveryOutcome.Sent:
                        return Results.Ok(await LoadTelegramAsync(database, bot, user.Id, cancellationToken));
                    case DeliveryOutcome.NotLinked:
                        return Results.Problem(title: "Connect Telegram first.", statusCode: StatusCodes.Status409Conflict);
                    default:
                        // The failure is on the channel; settings reads it from there.
                        return Results.Problem(
                            title: "Telegram did not accept the message.",
                            detail: result.Failure?.ToString(),
                            statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notifications.MapDelete("/channels/telegram", async (
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

                var channel = await FindAsync(database, user.Id, cancellationToken);
                if (channel is not null)
                {
                    database.NotificationChannels.Remove(channel);
                }

                // A code issued for a link the owner has now abandoned must not connect anything later.
                await database.NotificationLinkCodes.Where(row => row.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static IResult NotConfigured() => Results.Problem(
        title: "Telegram is not configured.",
        detail: "TELEGRAM_BOT_TOKEN is not set on this deployment.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://taxes-ua/problems/telegram-not-configured");

    private static Task<NotificationChannel?> FindAsync(AppDbContext database, string userId, CancellationToken cancellationToken) =>
        database.NotificationChannels.FirstOrDefaultAsync(
            row => row.UserId == userId && row.Kind == NotificationChannelKind.Telegram, cancellationToken);

    private static async Task<NotificationChannelResponse> LoadTelegramAsync(
        AppDbContext database, TelegramBot bot, string userId, CancellationToken cancellationToken)
    {
        var channel = await database.NotificationChannels.AsNoTracking().FirstOrDefaultAsync(
            row => row.UserId == userId && row.Kind == NotificationChannelKind.Telegram, cancellationToken);

        return ToResponse(bot, channel);
    }

    private static NotificationChannelResponse ToResponse(TelegramBot bot, NotificationChannel? channel) => new(
        NotificationChannelKind.Telegram,
        bot.IsConfigured,
        channel is not null,
        channel?.Enabled ?? false,
        channel?.LinkedAt,
        channel?.LastDeliveryAt,
        channel?.LastFailure,
        channel?.LastFailureAt);
}

internal sealed record TelegramToggleRequest(bool Enabled);

internal sealed record TelegramConnectResponse(string Url, DateTimeOffset ExpiresAt);

// The chat id itself is never sent to the browser; settings has no use for it.
internal sealed record NotificationChannelResponse(
    NotificationChannelKind Kind,
    bool Available,
    bool Linked,
    bool Enabled,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? LastDeliveryAt,
    DeliveryFailure? LastFailure,
    DateTimeOffset? LastFailureAt);
