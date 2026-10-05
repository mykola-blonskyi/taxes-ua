

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The one way a message reaches an owner's email address: the confirmation, the test button and the
/// reminders use it, through <see cref="ChannelDelivery"/>'s retries and failure record. The message
/// is built from the address on the channel row, so what is sent to is what the owner confirmed.
/// </summary>
internal sealed class EmailDelivery(ChannelDelivery delivery, EmailSettings settings, IEmailTransport transport)
{
    public Task<DeliveryResult> SendAsync(
        string userId, DeliveryPurpose purpose, Func<string, EmailMessage> message, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return Task.FromResult(new DeliveryResult(DeliveryOutcome.NotAvailable));
        }

        return delivery.SendAsync(
            NotificationChannelKind.Email,
            userId,
            purpose,
            (channel, token) => transport.SendAsync(message(channel.Address), token),
            cancellationToken);
    }
}
