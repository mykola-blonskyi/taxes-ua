using System.Globalization;
using System.Text.Json.Serialization;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// A channel's settings as the backup file carries them. Link codes, delivery bookkeeping and the poll
/// offset are not here: a restored channel starts with a clean delivery record, and a code is never
/// written to a file.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record NotificationChannelBackup(
    NotificationChannelKind Kind,
    string Address,
    bool Enabled,
    DateTimeOffset LinkedAt,
    DateTimeOffset? ConfirmedAt)
{
    public static NotificationChannelBackup From(NotificationChannel row) =>
        new(row.Kind, row.Address, row.Enabled, row.LinkedAt, row.ConfirmedAt);

    public (string Key, string Message)? Error() => Kind switch
    {
        _ when !Enum.IsDefined(Kind) => ("kind", "kind must name a channel kind."),
        NotificationChannelKind.Telegram when !long.TryParse(Address, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) =>
            ("address", "A Telegram address must be a chat id."),
        NotificationChannelKind.Email when !EmailTexts.TryNormalize(Address, out _) =>
            ("address", "An email address must be a plain address such as name@example.com."),
        NotificationChannelKind.Telegram when ConfirmedAt is null => ("confirmedAt", "A Telegram channel is always confirmed."),
        _ when Enabled && ConfirmedAt is null => ("enabled", "A channel cannot be enabled before it is confirmed."),
        _ => null,
    };

    public NotificationChannel ToEntity(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Kind = Kind,
        Address = Address,
        // A file proves nothing about a mailbox, so an email address comes back unconfirmed and off, and
        // the owner sends the link again (ADR-022). A Telegram chat id is only ever linked by pressing Start.
        Enabled = Kind != NotificationChannelKind.Email && Enabled,
        LinkedAt = LinkedAt.ToUniversalTime(),
        ConfirmedAt = Kind == NotificationChannelKind.Email ? null : ConfirmedAt?.ToUniversalTime(),
    };
}
