using System.Globalization;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// An incident as plain text in the owner's language: a first line that says it, a line of what to do,
/// and a link to the settings tab that fixes it when the app's address is known. Anything but "ru" is
/// Ukrainian, like <see cref="ReminderTexts"/>.
/// </summary>
internal static class IncidentTexts
{
    public static ReminderMessage Render(Incident incident, string locale, string? appUrl)
    {
        var russian = locale == "ru";
        var since = incident.Since is { } at ? at.KyivDate().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "?";

        var (subject, advice) = incident.Kind switch
        {
            IncidentKind.SyncStale => russian
                ? ($"Monobank: синхронизация не работает, последняя успешная {since}.",
                    "Доходы в приложении могут быть неполными. Проверьте подключение monobank.")
                : ($"Monobank: синхронізація не працює, остання успішна {since}.",
                    "Доходи в застосунку можуть бути неповними. Перевірте підключення monobank."),
            IncidentKind.TokenRejected => russian
                ? ("Monobank отклонил токен, синхронизация остановлена.",
                    "Доходы в приложении не обновляются. Создайте новый токен и сохраните его в настройках.")
                : ("Monobank відхилив токен, синхронізацію зупинено.",
                    "Доходи в застосунку не оновлюються. Створіть новий токен і збережіть його в налаштуваннях."),
            IncidentKind.TokenUnreadable => russian
                ? ("Токен monobank не удалось прочитать, синхронизация остановлена.",
                    "Вероятно, потерян ключ шифрования. Сохраните токен в настройках заново.")
                : ("Токен monobank не вдалося прочитати, синхронізацію зупинено.",
                    "Імовірно, втрачено ключ шифрування. Збережіть токен у налаштуваннях заново."),
            _ => throw new ArgumentOutOfRangeException(nameof(incident), incident.Kind, "Unknown incident kind."),
        };

        var lines = new List<string> { subject, advice };
        if (appUrl is not null)
        {
            lines.Add($"{(russian ? "Открыть настройки" : "Відкрити налаштування")}: {appUrl}settings?tab=monobank");
        }

        return new ReminderMessage(subject, string.Join('\n', lines));
    }
}
