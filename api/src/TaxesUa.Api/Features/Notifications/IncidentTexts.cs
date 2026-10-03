using System.Globalization;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// An incident as plain text in the owner's language: a first line that says it, a line of what to do,
/// and a link to the settings tab that fixes it when the kind has one and the app's address is known.
/// Anything but "ru" is Ukrainian, like <see cref="ReminderTexts"/>.
/// </summary>
internal static class IncidentTexts
{
    private const string MonobankTab = "monobank";

    private const string TaxYearsTab = "taxYears";

    public static ReminderMessage Render(Incident incident, string locale, string? appUrl)
    {
        var russian = locale == "ru";
        var since = incident.Since is { } at ? at.KyivDate().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "?";

        var (subject, advice, settingsTab) = incident.Kind switch
        {
            IncidentKind.SyncStale => russian
                ? ($"Monobank: синхронизация не работает, обновлений нет с {since}.",
                    "Доходы в приложении могут быть неполными. Проверьте подключение monobank.", (string?)MonobankTab)
                : ($"Monobank: синхронізація не працює, оновлень немає з {since}.",
                    "Доходи в застосунку можуть бути неповними. Перевірте підключення monobank.", MonobankTab),
            IncidentKind.TokenRejected => russian
                ? ("Monobank отклонил токен, синхронизация остановлена.",
                    "Доходы в приложении не обновляются. Создайте новый токен и сохраните его в настройках.", MonobankTab)
                : ("Monobank відхилив токен, синхронізацію зупинено.",
                    "Доходи в застосунку не оновлюються. Створіть новий токен і збережіть його в налаштуваннях.", MonobankTab),
            IncidentKind.TokenUnreadable => russian
                ? ("Токен monobank не удалось прочитать, синхронизация остановлена.",
                    "Вероятно, потерян ключ шифрования. Сохраните токен в настройках заново.", MonobankTab)
                : ("Токен monobank не вдалося прочитати, синхронізацію зупинено.",
                    "Імовірно, втрачено ключ шифрування. Збережіть токен у налаштуваннях заново.", MonobankTab),
            IncidentKind.RestoreCheckFailed => russian
                ? (incident.Since is null
                        ? "Резервные копии: проверка восстановления ещё ни разу не прошла."
                        : $"Резервные копии: проверка восстановления не проходит, последняя успешная {since}.",
                    "Восстановить базу из копий может быть невозможно. Посмотрите журнал сервиса backup в Coolify.",
                    null)
                : (incident.Since is null
                        ? "Резервні копії: перевірка відновлення ще жодного разу не пройшла."
                        : $"Резервні копії: перевірка відновлення не проходить, остання успішна {since}.",
                    "Відновити базу з копій може бути неможливо. Перегляньте журнал сервісу backup у Coolify.",
                    null),
            IncidentKind.NewTaxYear => russian
                ? ($"Налоговый {incident.Year} год: проверьте и подтвердите параметры.",
                    "Минимальная зарплата, база и ставка ЕСВ, лимиты. Можно клонировать прошлый год и поправить цифры.", TaxYearsTab)
                : ($"Податковий {incident.Year} рік: перевірте й підтвердьте параметри.",
                    "Мінімальна зарплата, база й ставка ЄСВ, ліміти. Можна клонувати попередній рік і виправити цифри.", TaxYearsTab),
            IncidentKind.MissingTaxYear => russian
                ? ($"Параметры {incident.Year} года не заданы, баланс недоступен.",
                    "Клонируйте прошлый год на вкладке налоговых лет, поправьте цифры и отметьте год проверенным.", TaxYearsTab)
                : ($"Параметри {incident.Year} року не задано, баланс недоступний.",
                    "Клонуйте попередній рік на вкладці податкових років, виправте цифри й позначте рік перевіреним.", TaxYearsTab),
            _ => throw new ArgumentOutOfRangeException(nameof(incident), incident.Kind, "Unknown incident kind."),
        };

        var lines = new List<string> { subject, advice };
        if (appUrl is not null && settingsTab is not null)
        {
            lines.Add($"{(russian ? "Открыть настройки" : "Відкрити налаштування")}: {appUrl}settings?tab={settingsTab}");
        }

        return new ReminderMessage(subject, string.Join('\n', lines));
    }
}
