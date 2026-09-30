using System.Text.RegularExpressions;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The configured bot: its token and the id that token starts with. The token is a secret that
/// Telegram puts in the request path, so it lives only here and in <see cref="TelegramClient"/>, and
/// has no ToString to leak through a log line. Without a valid token the channel is unavailable and
/// nothing polls.
/// </summary>
internal sealed partial class TelegramBot
{
    public TelegramBot(IConfiguration configuration, ILogger<TelegramBot> logger)
    {
        var token = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var match = TokenShape().Match(token);
        if (!match.Success)
        {
            logger.LogWarning("TELEGRAM_BOT_TOKEN is set but is not in the form <bot id>:<secret>; Telegram stays unavailable.");
            return;
        }

        Token = token;
        BotId = long.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool IsConfigured => Token is not null;

    public long BotId { get; }

    internal string? Token { get; }

    [GeneratedRegex(@"^(?<id>\d{1,18}):[A-Za-z0-9_-]+$")]
    private static partial Regex TokenShape();
}
