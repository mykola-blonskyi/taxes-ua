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
                EmailSettings email,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new[]
                {
                    await LoadAsync(database, bot, email, NotificationChannelKind.Telegram, user.Id, cancellationToken),
                    await LoadAsync(database, bot, email, NotificationChannelKind.Email, user.Id, cancellationToken),
                });
            })
            .Produces<NotificationChannelResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        MapTelegram(notifications);
        MapEmail(notifications);

        return routes;
    }

    private static void MapTelegram(RouteGroupBuilder notifications)
    {
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
                    return TelegramNotConfigured();
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

        notifications.MapPut("/channels/telegram", (
                ChannelToggleRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                HttpContext http,
                CancellationToken cancellationToken) =>
            ToggleAsync(request, NotificationChannelKind.Telegram, users, database, bot, email, http, cancellationToken))
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        notifications.MapPost("/channels/telegram/test", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                TelegramDelivery delivery,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!bot.IsConfigured)
                {
                    return TelegramNotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var locale = (await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken)).Locale;
                var result = await delivery.SendAsync(user.Id, TelegramTexts.Test(locale), evenIfDisabled: true, cancellationToken);

                return await TestAnswerAsync(
                    result, "Telegram", database, bot, email, NotificationChannelKind.Telegram, user.Id, cancellationToken);
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notifications.MapDelete("/channels/telegram", (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            RemoveAsync(NotificationChannelKind.Telegram, users, database, http, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    // The address is added unconfirmed and the only thing sent to it is the confirmation link. Adding a
    // different address replaces the row, so the old one stops receiving at once; the link in an
    // earlier email names the old address and no longer opens anything.
    private static void MapEmail(RouteGroupBuilder notifications)
    {
        notifications.MapPost("/channels/email", async (
                EmailAddressRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                EmailDelivery delivery,
                EmailConfirmation confirmation,
                AppLink link,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!email.IsConfigured)
                {
                    return EmailNotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                if (!EmailTexts.TryNormalize(request.Address, out var address))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["address"] = ["Enter an email address such as name@example.com."],
                    });
                }

                var channel = await FindAsync(database, user.Id, NotificationChannelKind.Email, cancellationToken);
                if (channel is null)
                {
                    channel = new NotificationChannel { Id = Guid.NewGuid(), UserId = user.Id, Kind = NotificationChannelKind.Email };
                    database.NotificationChannels.Add(channel);
                }

                if (channel.ConfirmedAt is not null && string.Equals(channel.Address, address, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Ok(ToResponse(bot, email, NotificationChannelKind.Email, channel));
                }

                channel.Address = address;
                channel.Enabled = false;
                channel.LinkedAt = time.GetUtcNow();
                channel.ConfirmedAt = null;
                channel.LastDeliveryAt = null;
                channel.LastFailure = null;
                channel.LastFailureAt = null;
                await database.SaveChangesAsync(cancellationToken);

                return await SendConfirmationAsync(
                    user.Id, database, bot, email, delivery, confirmation, link, cancellationToken);
            })
            .Produces<NotificationChannelResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notifications.MapPost("/channels/email/resend", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                EmailDelivery delivery,
                EmailConfirmation confirmation,
                AppLink link,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!email.IsConfigured)
                {
                    return EmailNotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var channel = await FindAsync(database, user.Id, NotificationChannelKind.Email, cancellationToken);
                if (channel is not { ConfirmedAt: null })
                {
                    return Results.Problem(title: "There is no address waiting for confirmation.", statusCode: StatusCodes.Status409Conflict);
                }

                return await SendConfirmationAsync(
                    user.Id, database, bot, email, delivery, confirmation, link, cancellationToken);
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // The link opens settings, which asks for this call under the owner's own session: a link that
        // changes state when merely fetched would be confirmed by a mail scanner, and one that works
        // for whoever holds it would not prove the owner read the mailbox they signed in with.
        notifications.MapPost("/channels/email/confirm", async (
                EmailConfirmRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                EmailConfirmation confirmation,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var (check, address) = confirmation.Read(request.Token ?? string.Empty, user.Id);
                if (check == ConfirmationCheck.Invalid)
                {
                    return Results.Problem(
                        title: "This confirmation link is not valid.",
                        statusCode: StatusCodes.Status400BadRequest,
                        type: "https://taxes-ua/problems/email-link-invalid");
                }

                var channel = await FindAsync(database, user.Id, NotificationChannelKind.Email, cancellationToken);
                if (channel is not null && channel.ConfirmedAt is not null
                    && string.Equals(channel.Address, address, StringComparison.OrdinalIgnoreCase))
                {
                    // Opened twice, or by a scanner first: already done.
                    return Results.Ok(ToResponse(bot, email, NotificationChannelKind.Email, channel));
                }

                if (check == ConfirmationCheck.Expired
                    || channel is null
                    || !string.Equals(channel.Address, address, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        title: "This confirmation link has expired or the address has since been changed.",
                        statusCode: StatusCodes.Status410Gone,
                        type: "https://taxes-ua/problems/email-link-expired");
                }

                channel.ConfirmedAt = time.GetUtcNow();
                channel.Enabled = true;
                channel.LastFailure = null;
                channel.LastFailureAt = null;
                await database.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToResponse(bot, email, NotificationChannelKind.Email, channel));
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status410Gone);

        notifications.MapPut("/channels/email", (
                ChannelToggleRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                HttpContext http,
                CancellationToken cancellationToken) =>
            ToggleAsync(request, NotificationChannelKind.Email, users, database, bot, email, http, cancellationToken))
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        notifications.MapPost("/channels/email/test", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TelegramBot bot,
                EmailSettings email,
                EmailDelivery delivery,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!email.IsConfigured)
                {
                    return EmailNotConfigured();
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var locale = (await SettingsEndpoints.LoadOrDefaultAsync(database, user.Id, cancellationToken)).Locale;
                var result = await delivery.SendAsync(
                    user.Id, DeliveryPurpose.Test, address => EmailTexts.Test(address, locale), cancellationToken);

                return await TestAnswerAsync(
                    result, "The email server", database, bot, email, NotificationChannelKind.Email, user.Id, cancellationToken);
            })
            .Produces<NotificationChannelResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notifications.MapDelete("/channels/email", (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            RemoveAsync(NotificationChannelKind.Email, users, database, http, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    // Issues a fresh link for the address now on the row and sends it. A failure is on the channel for
    // settings to show, and the call answers 502 like the test button does.
    private static async Task<IResult> SendConfirmationAsync(
        string userId,
        AppDbContext database,
        TelegramBot bot,
        EmailSettings email,
        EmailDelivery delivery,
        EmailConfirmation confirmation,
        AppLink link,
        CancellationToken cancellationToken)
    {
        var locale = (await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken)).Locale;
        var result = await delivery.SendAsync(
            userId,
            DeliveryPurpose.Confirmation,
            address => EmailTexts.Confirmation(
                address, locale, $"{link.Url}settings?tab=notifications&confirmEmail={confirmation.Issue(userId, address).Token}"),
            cancellationToken);

        return await TestAnswerAsync(
            result, "The email server", database, bot, email, NotificationChannelKind.Email, userId, cancellationToken);
    }

    private static async Task<IResult> ToggleAsync(
        ChannelToggleRequest request,
        NotificationChannelKind kind,
        UserManager<ApplicationUser> users,
        AppDbContext database,
        TelegramBot bot,
        EmailSettings email,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var channel = await FindAsync(database, user.Id, kind, cancellationToken);
        if (channel is null)
        {
            return Results.Problem(title: $"Connect {kind} first.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.Enabled && channel.ConfirmedAt is null)
        {
            return Results.Problem(title: "Confirm the address first.", statusCode: StatusCodes.Status409Conflict);
        }

        channel.Enabled = request.Enabled;
        if (request.Enabled)
        {
            channel.LastFailure = null;
            channel.LastFailureAt = null;
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(bot, email, kind, channel));
    }

    private static async Task<IResult> RemoveAsync(
        NotificationChannelKind kind,
        UserManager<ApplicationUser> users,
        AppDbContext database,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var channel = await FindAsync(database, user.Id, kind, cancellationToken);
        if (channel is not null)
        {
            database.NotificationChannels.Remove(channel);
        }

        // A code issued for a link the owner has now abandoned must not connect anything later. An
        // email confirmation link needs no cleanup: it names an address and finds no channel.
        if (kind == NotificationChannelKind.Telegram)
        {
            await database.NotificationLinkCodes.Where(row => row.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> TestAnswerAsync(
        DeliveryResult result,
        string service,
        AppDbContext database,
        TelegramBot bot,
        EmailSettings email,
        NotificationChannelKind kind,
        string userId,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case DeliveryOutcome.Sent:
                return Results.Ok(await LoadAsync(database, bot, email, kind, userId, cancellationToken));
            case DeliveryOutcome.NotLinked:
                return Results.Problem(title: $"Connect {kind} first.", statusCode: StatusCodes.Status409Conflict);
            case DeliveryOutcome.NotConfirmed:
                return Results.Problem(title: "Confirm the address first.", statusCode: StatusCodes.Status409Conflict);
            default:
                // The failure is on the channel; settings reads it from there.
                return Results.Problem(
                    title: $"{service} did not accept the message.",
                    detail: result.Failure?.ToString(),
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult TelegramNotConfigured() => Results.Problem(
        title: "Telegram is not configured.",
        detail: "TELEGRAM_BOT_TOKEN is not set on this deployment.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://taxes-ua/problems/telegram-not-configured");

    private static IResult EmailNotConfigured() => Results.Problem(
        title: "Email is not configured.",
        detail: "The SMTP settings (SMTP_HOST, SMTP_FROM and the rest) are not set, or are invalid, on this deployment.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://taxes-ua/problems/email-not-configured");

    private static Task<NotificationChannel?> FindAsync(
        AppDbContext database, string userId, NotificationChannelKind kind, CancellationToken cancellationToken) =>
        database.NotificationChannels.FirstOrDefaultAsync(row => row.UserId == userId && row.Kind == kind, cancellationToken);

    private static async Task<NotificationChannelResponse> LoadAsync(
        AppDbContext database,
        TelegramBot bot,
        EmailSettings email,
        NotificationChannelKind kind,
        string userId,
        CancellationToken cancellationToken)
    {
        var channel = await database.NotificationChannels.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Kind == kind, cancellationToken);

        return ToResponse(bot, email, kind, channel);
    }

    private static NotificationChannelResponse ToResponse(
        TelegramBot bot, EmailSettings email, NotificationChannelKind kind, NotificationChannel? channel) => new(
        kind,
        kind == NotificationChannelKind.Telegram ? bot.IsConfigured : email.IsConfigured,
        channel is not null,
        channel?.ConfirmedAt is not null,
        channel?.Enabled ?? false,
        kind == NotificationChannelKind.Email ? channel?.Address : null,
        channel?.LinkedAt,
        channel?.LastDeliveryAt,
        channel?.LastFailure,
        channel?.LastFailureAt);
}

internal sealed record ChannelToggleRequest(bool Enabled);

internal sealed record EmailAddressRequest(string? Address);

internal sealed record EmailConfirmRequest(string? Token);

internal sealed record TelegramConnectResponse(string Url, DateTimeOffset ExpiresAt);

// A Telegram chat id is never sent to the browser; settings has no use for it. An email address is, since
// the owner has to see which one is waiting for confirmation or receiving.
internal sealed record NotificationChannelResponse(
    NotificationChannelKind Kind,
    bool Available,
    bool Linked,
    bool Confirmed,
    bool Enabled,
    string? Address,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? LastDeliveryAt,
    DeliveryFailure? LastFailure,
    DateTimeOffset? LastFailureAt);
