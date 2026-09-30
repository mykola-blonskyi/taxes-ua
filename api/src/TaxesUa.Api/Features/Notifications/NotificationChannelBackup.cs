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
    DateTimeOffset LinkedAt)
{
    public static NotificationChannelBackup From(NotificationChannel row) => new(row.Kind, row.Address, row.Enabled, row.LinkedAt);

    public (string Key, string Message)? Error() => Kind switch
    {
        _ when !Enum.IsDefined(Kind) => ("kind", "kind must name a channel kind."),
        NotificationChannelKind.Telegram when !long.TryParse(Address, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) =>
            ("address", "A Telegram address must be a chat id."),
        _ => null,
    };

    public NotificationChannel ToEntity(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Kind = Kind,
        Address = Address,
        Enabled = Enabled,
        LinkedAt = LinkedAt.ToUniversalTime(),
    };
}
