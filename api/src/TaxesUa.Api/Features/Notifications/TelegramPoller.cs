using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// One round of long polling: read the updates after the persisted offset, act on each, and move the
/// offset past it in the same save as the effect (ADR-015). A restart therefore neither replays an
/// update nor skips one. Only <c>/start &lt;code&gt;</c> from a private chat does anything; every
/// other private message gets one short reply.
/// </summary>
internal sealed partial class TelegramPoller(
    TelegramBot bot,
    TelegramClient client,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<TelegramPoller> logger)
{
    public static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(30);

    // Telegram answers getUpdates with 409 while a webhook is set on the bot (or another process is
    // polling it). Removing the webhook is tried once per process: if the 409 is a second poller's,
    // deleting a webhook that is not there changes nothing, and asking again every round would not help.
    private bool _webhookCleared;

    // False when Telegram could not be read, so the caller backs off instead of hammering it.
    public async Task<bool> PollOnceAsync(TimeSpan longPoll, CancellationToken cancellationToken)
    {
        long offset;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            offset = await database.TelegramPollStates
                .Where(state => state.BotId == bot.BotId)
                .Select(state => state.NextOffset)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var result = await client.GetUpdatesAsync(offset, longPoll, cancellationToken);
        if (!result.IsOk)
        {
            logger.LogWarning("Telegram updates could not be read: {Failure}.", result.Failure);
            if (result.Status == HttpStatusCode.Conflict && !_webhookCleared)
            {
                _webhookCleared = true;
                var cleared = await client.DeleteWebhookAsync(cancellationToken);
                logger.LogWarning(
                    "Telegram answered 409: the bot has a webhook set or is polled elsewhere. Removing the webhook {Outcome}.",
                    cleared.IsOk ? "succeeded" : "failed: " + cleared.Failure);
            }

            return false;
        }

        foreach (var update in result.Value!.OrderBy(update => update.UpdateId))
        {
            await HandleAsync(update, cancellationToken);
        }

        return true;
    }

    private async Task HandleAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        Reply? reply;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            reply = await ActAsync(database, scope.ServiceProvider.GetRequiredService<TelegramLinking>(), update, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }

        if (reply is not null)
        {
            var sent = await client.SendMessageAsync(reply.ChatId, reply.Text, cancellationToken);
            if (!sent.IsOk)
            {
                logger.LogWarning("A Telegram reply could not be sent: {Failure}.", sent.Failure);
            }
        }
    }

    private async Task<Reply?> ActAsync(
        AppDbContext database, TelegramLinking linking, TelegramUpdate update, CancellationToken cancellationToken)
    {
        await AdvanceOffsetAsync(database, update.UpdateId, cancellationToken);

        if (update.Message is not { IsPrivate: true } message)
        {
            return null;
        }

        var chatId = message.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (message.Text is { } text && Start().Match(text) is { Success: true } start)
        {
            var userId = await linking.RedeemAsync(start.Groups["code"].Value, cancellationToken);
            if (userId is null)
            {
                return new Reply(chatId, TelegramTexts.CodeRejected(TelegramTexts.LocaleOfLanguage(message.Language)));
            }

            var now = time.GetUtcNow();
            var channel = await database.NotificationChannels.FirstOrDefaultAsync(
                row => row.UserId == userId && row.Kind == NotificationChannelKind.Telegram, cancellationToken);
            if (channel is null)
            {
                channel = new NotificationChannel { Id = Guid.NewGuid(), UserId = userId, Kind = NotificationChannelKind.Telegram };
                database.NotificationChannels.Add(channel);
            }

            channel.Address = chatId;
            channel.Enabled = true;
            channel.LinkedAt = now;
            channel.ConfirmedAt = now;
            channel.LastFailure = null;
            channel.LastFailureAt = null;

            return new Reply(chatId, TelegramTexts.Linked(await LocaleOfOwnerAsync(database, userId, cancellationToken)));
        }

        var known = await database.NotificationChannels.AsNoTracking()
            .Where(row => row.Kind == NotificationChannelKind.Telegram && row.Address == chatId)
            .Select(row => row.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        var locale = known is null
            ? TelegramTexts.LocaleOfLanguage(message.Language)
            : await LocaleOfOwnerAsync(database, known, cancellationToken);

        return new Reply(chatId, TelegramTexts.NotUnderstood(locale));
    }

    private async Task AdvanceOffsetAsync(AppDbContext database, long updateId, CancellationToken cancellationToken)
    {
        var state = await database.TelegramPollStates.FindAsync([bot.BotId], cancellationToken);
        if (state is null)
        {
            database.TelegramPollStates.Add(new TelegramPollState { BotId = bot.BotId, NextOffset = updateId + 1 });
        }
        else if (updateId + 1 > state.NextOffset)
        {
            state.NextOffset = updateId + 1;
        }
    }

    private static async Task<string> LocaleOfOwnerAsync(AppDbContext database, string userId, CancellationToken cancellationToken) =>
        await database.Settings.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.Locale)
            .FirstOrDefaultAsync(cancellationToken) ?? "uk";

    // "/start CODE" or "/start@botname CODE"; the code alone is what the deep link carries.
    [GeneratedRegex(@"^/start(?:@\w+)?\s+(?<code>[A-Za-z0-9_-]{1,64})\s*$")]
    private static partial Regex Start();

    private sealed record Reply(string ChatId, string Text);
}
