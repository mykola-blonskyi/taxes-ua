namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// What the bot says outside reminders, in the two interface languages. Anything but "ru" is
/// Ukrainian, the app's default.
/// </summary>
internal static class TelegramTexts
{
    public static string Linked(string locale) => locale == "ru"
        ? "Telegram подключён. Сюда будут приходить напоминания о сроках уплаты налогов."
        : "Telegram підключено. Сюди надходитимуть нагадування про терміни сплати податків.";

    public static string CodeRejected(string locale) => locale == "ru"
        ? "Ссылка недействительна или устарела. Откройте настройки приложения и нажмите «Подключить Telegram» ещё раз."
        : "Посилання недійсне або застаріло. Відкрийте налаштування застосунку й натисніть «Підключити Telegram» ще раз.";

    public static string NotUnderstood(string locale) => locale == "ru"
        ? "Этот бот только присылает напоминания. Чтобы подключить его, нажмите «Подключить Telegram» в настройках приложения."
        : "Цей бот лише надсилає нагадування. Щоб підключити його, натисніть «Підключити Telegram» у налаштуваннях застосунку.";

    public static string Test(string locale) => locale == "ru"
        ? "Тестовое сообщение: напоминания будут приходить сюда."
        : "Тестове повідомлення: нагадування надходитимуть сюди.";

    public static string LocaleOfLanguage(string? language) =>
        language is not null && language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "uk";
}
