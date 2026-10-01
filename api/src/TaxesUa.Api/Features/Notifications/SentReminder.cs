using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// A reminder claimed for one channel, written before the message is sent so two runs cannot both
/// send it and a restart mid-send does not send it again: at most once, never twice. A claim without
/// <c>DeliveredAt</c> is one whose send failed for good or was cut off. A later message for the same
/// date and offset goes out only for a kind no earlier claim covered.
/// </summary>
internal sealed class SentReminder
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public ReminderKinds Kinds { get; set; }

    public ReminderOffset Offset { get; set; }

    public NotificationChannelKind Channel { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
}
