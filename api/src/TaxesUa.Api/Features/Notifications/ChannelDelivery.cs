using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Notifications;

internal enum DeliveryOutcome
{
    Sent,
    NotAvailable,
    NotLinked,
    NotConfirmed,
    Disabled,
    Failed,
}

internal sealed record DeliveryResult(DeliveryOutcome Outcome, DeliveryFailure? Failure = null);

// Why a message is being sent decides which channels may receive it. An address waiting for its
// confirmation receives the Confirmation and nothing else; the test button ignores the toggle.
internal enum DeliveryPurpose
{
    Reminder,
    Test,
    Confirmation,
}

/// <summary>One try at handing a message to a channel's service. A null failure means it accepted it.</summary>
internal readonly record struct DeliveryAttempt(DeliveryFailure? Failure = null, TimeSpan? RetryAfter = null)
{
    public bool IsOk => Failure is null;
}

/// <summary>
/// What every channel shares about sending: who may be sent to, the retries, and the record on the
/// channel that settings shows. A transient failure is retried three times after the first attempt,
/// waiting 1, 2 and 4 seconds (or what the service's 429 asks for, when that is longer and not
/// absurd); then the failure is written on the channel. Blocked switches the channel off. A timeout
/// is not retried, since the message may have been delivered. A channel contributes only the attempt.
/// </summary>
internal sealed class ChannelDelivery(AppDbContext database, TimeProvider time, ILogger<ChannelDelivery> logger)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    // A 429 that asks for longer than this is not waited out inside a request or a reminder run.
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    public async Task<DeliveryResult> SendAsync(
        NotificationChannelKind kind,
        string userId,
        DeliveryPurpose purpose,
        Func<NotificationChannel, CancellationToken, Task<DeliveryAttempt>> attempt,
        CancellationToken cancellationToken)
    {
        var channel = await database.NotificationChannels.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Kind == kind, cancellationToken);
        if (channel is null)
        {
            return new DeliveryResult(DeliveryOutcome.NotLinked);
        }

        if (purpose != DeliveryPurpose.Confirmation && channel.ConfirmedAt is null)
        {
            return new DeliveryResult(DeliveryOutcome.NotConfirmed);
        }

        if (purpose == DeliveryPurpose.Reminder && !channel.Enabled)
        {
            return new DeliveryResult(DeliveryOutcome.Disabled);
        }

        for (var tried = 0; ; tried++)
        {
            var result = await attempt(channel, cancellationToken);
            if (result.IsOk)
            {
                await RecordAsync(channel.Id, null, cancellationToken);
                return new DeliveryResult(DeliveryOutcome.Sent);
            }

            var failure = result.Failure!.Value;
            var wait = tried < Backoff.Length ? Backoff[tried] : TimeSpan.Zero;
            if (result.RetryAfter is { } asked)
            {
                wait = asked > wait ? asked : wait;
            }

            if (!failure.IsTransient() || tried >= Backoff.Length || wait > MaxRetryAfter)
            {
                logger.LogWarning(
                    "{Channel} delivery for owner {UserId} failed after {Attempts} attempt(s): {Failure}.",
                    kind, userId, tried + 1, failure);
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
            logger.LogInformation("A {Channel} channel was disconnected while a message was being delivered.", channel.Kind);
        }
    }
}
