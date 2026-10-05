

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The one way a message reaches an owner's Telegram chat: the test button and the reminders use it,
/// through <see cref="ChannelDelivery"/>'s retries and failure record. A 403 means the owner blocked
/// the bot: no retry, and the channel is switched off.
/// </summary>
internal sealed class TelegramDelivery(ChannelDelivery delivery, TelegramBot bot, TelegramClient client)
{
    public Task<DeliveryResult> SendAsync(
        string userId, string text, bool evenIfDisabled, CancellationToken cancellationToken)
    {
        if (!bot.IsConfigured)
        {
            return Task.FromResult(new DeliveryResult(DeliveryOutcome.NotAvailable));
        }

        return delivery.SendAsync(
            NotificationChannelKind.Telegram,
            userId,
            evenIfDisabled ? DeliveryPurpose.Test : DeliveryPurpose.Reminder,
            async (channel, token) =>
            {
                var result = await client.SendMessageAsync(channel.Address, text, token);
                return new DeliveryAttempt(result.Failure, result.RetryAfter);
            },
            cancellationToken);
    }
}
