using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The one-time code behind a "Connect" deep link. Only the SHA-256 of the code is stored, so the
/// database never holds something that can link a chat; a code of 192 random bits needs no salt.
/// </summary>
internal sealed class NotificationLinkCode
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public NotificationChannelKind Kind { get; set; }

    public byte[] CodeHash { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Where the bot's update stream was read up to. Update ids belong to one bot, so the row is keyed by
/// the bot's id and a different token starts from its own beginning.
/// </summary>
internal sealed class TelegramPollState
{
    public long BotId { get; set; }

    public long NextOffset { get; set; }
}
