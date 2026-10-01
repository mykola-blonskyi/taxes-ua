using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Notifications;

internal enum DeliveryOutcome
{
    Sent,
    NotAvailable,
    NotLinked,
    Disabled,
    Failed,
}

internal sealed record DeliveryResult(DeliveryOutcome Outcome, DeliveryFailure? Failure = null);

/// <summary>
/// The one way a message reaches an owner's Telegram chat: the test button and the reminders use it.
/// A transient failure is retried three times after the first attempt, waiting 1, 2 and 4 seconds (or
/// what Telegram's 429 asks for, when that is longer and not absurd); then the failure is written on
/// the channel for settings to show. A 403 means the owner blocked the bot: no retry, and the channel
/// is switched off. A timeout is not retried either, since the message may have been delivered.
/// </summary>
internal sealed class TelegramDelivery(
    AppDbContext database,
    TelegramBot bot,
    TelegramClient client,
    TimeProvider time,
    ILogger<TelegramDelivery> logger)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    // A 429 that asks for longer than this is not waited out inside a request or a reminder run.
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    public async Task<DeliveryResult> SendAsync(
        string userId, string text, bool evenIfDisabled, CancellationToken cancellationToken)
    {
        if (!bot.IsConfigured)
        {
            return new DeliveryResult(DeliveryOutcome.NotAvailable);
        }

        var channel = await database.NotificationChannels.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Kind == NotificationChannelKind.Telegram, cancellationToken);
        if (channel is null)
        {
            return new DeliveryResult(DeliveryOutcome.NotLinked);
        }

        if (!channel.Enabled && !evenIfDisabled)
        {
            return new DeliveryResult(DeliveryOutcome.Disabled);
        }

        for (var attempt = 0; ; attempt++)
        {
            var result = await client.SendMessageAsync(channel.Address, text, cancellationToken);
            if (result.IsOk)
            {
                await RecordAsync(channel.Id, null, cancellationToken);
                return new DeliveryResult(DeliveryOutcome.Sent);
            }

            var failure = result.Failure!.Value;
            var wait = attempt < Backoff.Length ? Backoff[attempt] : TimeSpan.Zero;
            if (result.RetryAfter is { } asked)
            {
                wait = asked > wait ? asked : wait;
            }

            if (!failure.IsTransient() || attempt >= Backoff.Length || wait > MaxRetryAfter)
            {
                logger.LogWarning("Telegram delivery for owner {UserId} failed after {Attempts} attempt(s): {Failure}.", userId, attempt + 1, failure);
                await RecordAsync(channel.Id, failure, cancellationToken);
                return new DeliveryResult(DeliveryOutcome.Failed, failure);
            }

            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The channel is read again because the owner may have disconnected or toggled it while the
    // message was being retried.
    private async Task RecordAsync(Guid channelId, DeliveryFailure? failure, CancellationToken cancellationToken)
    {
        var channel = await database.NotificationChannels.FirstOrDefaultAsync(row => row.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (failure is { } reason)
        {
            channel.LastFailure = reason;
            channel.LastFailureAt = now;
            if (reason == DeliveryFailure.Blocked)
            {
                channel.Enabled = false;
            }
        }
        else
        {
            channel.LastDeliveryAt = now;
            channel.LastFailure = null;
            channel.LastFailureAt = null;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Left Modified, the next save on this context (the reminder sender's) would fail again.
            database.Entry(channel).State = EntityState.Detached;
            logger.LogInformation("The Telegram channel was disconnected while a message was being delivered.");
        }
    }
}
