namespace TaxesUa.Api.Features.Notifications;

internal enum NotificationChannelKind
{
    Telegram,
    Email,
}

// Why the last delivery to a channel did not happen, kept as the name so settings can say it in the
// owner's language. Blocked is the only one that also switches the channel off.
internal enum DeliveryFailure
{
    Blocked,
    Rejected,
    RateLimited,
    Unreachable,
    Timeout,
    ServerError,
    Unreadable,
    Authentication,
}

internal static class DeliveryFailures
{
    // A timeout is not here: the message may have been delivered before the answer was lost, so it is
    // neither retried nor released for a later run.
    public static bool IsTransient(this DeliveryFailure failure) =>
        failure is DeliveryFailure.RateLimited or DeliveryFailure.Unreachable or DeliveryFailure.ServerError;
}

/// <summary>
/// Where one owner's reminders go. One row per owner and kind. Audited like settings, except the
/// delivery bookkeeping, which changes on every message and is not a decision the owner made.
/// </summary>
internal sealed class NotificationChannel
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public NotificationChannelKind Kind { get; set; }

    // The Telegram chat id, or the email address, as text.
    public string Address { get; set; } = string.Empty;

    // Never true before ConfirmedAt is set.
    public bool Enabled { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    // Null while an email address waits for its confirmation link: the only message it may receive is
    // that confirmation. A Telegram chat is confirmed by pressing Start, so it is set with LinkedAt.
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? LastDeliveryAt { get; set; }

    // Both set or both null. A later successful delivery clears them.
    public DeliveryFailure? LastFailure { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }
}
