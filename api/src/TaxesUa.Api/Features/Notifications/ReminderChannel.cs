using TaxesUa.Api.Features.Settings;


namespace TaxesUa.Api.Features.Notifications;

/// <summary>The reminder as a channel sends it. <c>Subject</c> is the first line of <c>Text</c>.</summary>
internal sealed record ReminderMessage(string Subject, string Text);

/// <summary>
/// One kind of place a reminder goes. <see cref="ReminderSender"/> knows channels only through this,
/// so a new kind is a new registration and not a change to the sender. An unavailable channel (no bot
/// token, say) is skipped without touching the database.
/// </summary>
internal interface IReminderChannel
{
    NotificationChannelKind Kind { get; }

    bool IsAvailable { get; }

    Task<DeliveryResult> SendAsync(string userId, ReminderMessage message, CancellationToken cancellationToken);
}

internal sealed class TelegramReminderChannel(TelegramBot bot, TelegramDelivery delivery) : IReminderChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.Telegram;

    public bool IsAvailable => bot.IsConfigured;

    public Task<DeliveryResult> SendAsync(string userId, ReminderMessage message, CancellationToken cancellationToken) =>
        delivery.SendAsync(userId, message.Text, evenIfDisabled: false, cancellationToken);
}

internal sealed class EmailReminderChannel(EmailSettings settings, EmailDelivery delivery) : IReminderChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    public bool IsAvailable => settings.IsConfigured;

    public Task<DeliveryResult> SendAsync(string userId, ReminderMessage message, CancellationToken cancellationToken) =>
        delivery.SendAsync(userId, DeliveryPurpose.Reminder, address => EmailTexts.Reminder(address, message), cancellationToken);
}
