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

        var (subject, advice, tab) = incident.Kind switch
        {
            IncidentKind.SyncStale => russian
                ? ($"Monobank: синхронизация не работает, обновлений нет с {since}.",
                    "Доходы в приложении могут быть неполными. Проверьте подключение monobank.", "monobank")
                : ($"Monobank: синхронізація не працює, оновлень немає з {since}.",
                    "Доходи в застосунку можуть бути неповними. Перевірте підключення monobank.", "monobank"),
            IncidentKind.TokenRejected => russian
                ? ("Monobank отклонил токен, синхронизация остановлена.",
                    "Доходы в приложении не обновляются. Создайте новый токен и сохраните его в настройках.", "monobank")
                : ("Monobank відхилив токен, синхронізацію зупинено.",
                    "Доходи в застосунку не оновлюються. Створіть новий токен і збережіть його в налаштуваннях.", "monobank"),
            IncidentKind.TokenUnreadable => russian
                ? ("Токен monobank не удалось прочитать, синхронизация остановлена.",
                    "Вероятно, потерян ключ шифрования. Сохраните токен в настройках заново.", "monobank")
                : ("Токен monobank не вдалося прочитати, синхронізацію зупинено.",
                    "Імовірно, втрачено ключ шифрування. Збережіть токен у налаштуваннях заново.", "monobank"),
            IncidentKind.NewTaxYear => russian
                ? ($"Налоговый {incident.Year} год: проверьте и подтвердите параметры.",
                    "Минимальная зарплата, база и ставка ЕСВ, лимиты. Можно клонировать прошлый год и поправить цифры.", "taxYears")
                : ($"Податковий {incident.Year} рік: перевірте й підтвердьте параметри.",
                    "Мінімальна зарплата, база й ставка ЄСВ, ліміти. Можна клонувати попередній рік і виправити цифри.", "taxYears"),
            IncidentKind.MissingTaxYear => russian
                ? ($"Параметры {incident.Year} года не заданы, баланс недоступен.",
                    "Клонируйте прошлый год на вкладке налоговых лет, поправьте цифры и отметьте год проверенным.", "taxYears")
                : ($"Параметри {incident.Year} року не задано, баланс недоступний.",
                    "Клонуйте попередній рік на вкладці податкових років, виправте цифри й позначте рік перевіреним.", "taxYears"),
            _ => throw new ArgumentOutOfRangeException(nameof(incident), incident.Kind, "Unknown incident kind."),
        };

        var lines = new List<string> { subject, advice };
        if (appUrl is not null)
        {
            lines.Add($"{(russian ? "Открыть настройки" : "Відкрити налаштування")}: {appUrl}settings?tab={tab}");
        }

        return new ReminderMessage(subject, string.Join('\n', lines));
    }
}
