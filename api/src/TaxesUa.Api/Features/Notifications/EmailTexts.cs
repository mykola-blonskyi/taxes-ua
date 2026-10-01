using System.Text.RegularExpressions;
using MimeKit;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// What the email channel says outside reminders, in the two interface languages (anything but "ru"
/// is Ukrainian), and the one place a plain text becomes the simple HTML sent beside it.
/// </summary>
internal static partial class EmailTexts
{
    public static EmailMessage Confirmation(string to, string locale, string url) => Both(to, locale == "ru"
        ? ("Подтвердите адрес для напоминаний о налогах",
            "Этот адрес добавлен в настройках приложения, чтобы получать напоминания о сроках уплаты налогов.\n"
            + $"Чтобы подтвердить его, откройте ссылку (она действует {EmailConfirmation.Lifetime.TotalHours:0} часа):\n{url}\n"
            + "Если вы этого не делали, просто проигнорируйте письмо: без подтверждения сюда ничего не придёт.")
        : ("Підтвердьте адресу для нагадувань про податки",
            "Цю адресу додано в налаштуваннях застосунку, щоб отримувати нагадування про терміни сплати податків.\n"
            + $"Щоб підтвердити її, відкрийте посилання (воно діє {EmailConfirmation.Lifetime.TotalHours:0} години):\n{url}\n"
            + "Якщо ви цього не робили, просто проігноруйте лист: без підтвердження сюди нічого не надійде."));

    public static EmailMessage Test(string to, string locale) => Both(to, locale == "ru"
        ? ("Тестовое сообщение", "Тестовое сообщение: напоминания будут приходить на этот адрес.")
        : ("Тестове повідомлення", "Тестове повідомлення: нагадування надходитимуть на цю адресу."));

    public static EmailMessage Reminder(string to, ReminderMessage reminder) =>
        new(to, reminder.Subject, reminder.Text, ToHtml(reminder.Text));

    private static EmailMessage Both(string to, (string Subject, string Text) content) =>
        new(to, content.Subject, content.Text, ToHtml(content.Text));

    // One paragraph per line, text escaped, and a bare http(s) address made a link. Nothing else is
    // markup, so a body can only ever be words and links.
    public static string ToHtml(string text)
    {
        var paragraphs = text.Split('\n').Select(line => $"<p>{Linkify(line)}</p>");

        return "<!DOCTYPE html><html><body style=\"font-family:sans-serif;line-height:1.5\">"
            + string.Concat(paragraphs)
            + "</body></html>";
    }

    private static string Linkify(string line)
    {
        var result = new System.Text.StringBuilder();
        var last = 0;
        foreach (Match match in Address().Matches(line))
        {
            result.Append(Encode(line[last..match.Index]));
            var url = Encode(match.Value);
            result.Append($"<a href=\"{url}\">{url}</a>");
            last = match.Index + match.Length;
        }

        return result.Append(Encode(line[last..])).ToString();
    }

    // Only the characters that can start markup or end an attribute; HtmlEncode would also turn every
    // Cyrillic letter into a numeric entity.
    private static string Encode(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    // A mailbox that is only an address: no name, no angle brackets, no list, no control characters.
    public static bool TryNormalize(string? input, out string address)
    {
        address = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 254 || !PlainAddress().IsMatch(trimmed) || !MailboxAddress.TryParse(trimmed, out var parsed)
            || parsed is not { } mailbox || mailbox.Address != trimmed)
        {
            return false;
        }

        address = trimmed;

        return true;
    }

    [GeneratedRegex(@"https?://[^\s<>""]+")]
    private static partial Regex Address();

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$")]
    private static partial Regex PlainAddress();
}
