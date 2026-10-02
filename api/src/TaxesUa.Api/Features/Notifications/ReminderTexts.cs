using System.Globalization;
using TaxesUa.Api.Features.Export;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// A reminder as plain text in the owner's language: a first line with the date and how far off it
/// is, one line per item, and the app's address when it is known. No markup, since sendMessage is
/// called without a parse mode. Anything but "ru" is Ukrainian, like <see cref="TelegramTexts"/>.
/// </summary>
internal static class ReminderTexts
{
    private static readonly Words Ukrainian = new(
        "Податки: строк",
        "через",
        ("день", "дні", "днів"),
        "завтра",
        "сьогодні",
        "минув учора",
        new()
        {
            [PaymentKind.SingleTax] = "Єдиний податок",
            [PaymentKind.MilitaryLevy] = "Військовий збір",
            [PaymentKind.Esv] = "ЄСВ",
        },
        "аванс за",
        "квартал",
        "Декларація за",
        "подати",
        ("Заява про обрання 3 групи єдиного податку: подати до", "від реєстрації", "інакше 3 група почнеться не раніше наступного кварталу"),
        ["січень", "лютий", "березень", "квітень", "травень", "червень", "липень", "серпень", "вересень", "жовтень", "листопад", "грудень"],
        "Відкрити застосунок");

    private static readonly Words Russian = new(
        "Налоги: срок",
        "через",
        ("день", "дня", "дней"),
        "завтра",
        "сегодня",
        "истёк вчера",
        new()
        {
            [PaymentKind.SingleTax] = "Единый налог",
            [PaymentKind.MilitaryLevy] = "Военный сбор",
            [PaymentKind.Esv] = "ЕСВ",
        },
        "аванс за",
        "квартал",
        "Декларация за",
        "подать",
        ("Заявление о выборе 3 группы единого налога: подать до", "от регистрации", "иначе 3 группа начнётся не раньше следующего квартала"),
        ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"],
        "Открыть приложение");

    private static readonly string[] Roman = ["I", "II", "III", "IV"];

    public static ReminderMessage Render(Reminder reminder, string locale, DateOnly today, string? appUrl)
    {
        var words = locale == "ru" ? Russian : Ukrainian;
        var date = Date(reminder.Date);
        var days = reminder.Date.DayNumber - today.DayNumber;
        var subject = days switch
        {
            > 1 => $"{words.Deadline} {date}, {words.In} {days} {Plural(days, words.Days)}.",
            1 => $"{words.Deadline} {words.Tomorrow}, {date}.",
            0 => $"{words.Deadline} {words.Today}, {date}.",
            _ => $"{words.Deadline} {words.PassedYesterday}, {date}.",
        };

        var lines = new List<string> { subject };
        lines.AddRange(reminder.Items.Select(item => Line(item, words)));
        if (appUrl is not null)
        {
            lines.Add($"{words.Open}: {appUrl}");
        }

        return new ReminderMessage(subject, string.Join('\n', lines));
    }

    private static string Line(ReminderItem item, Words words) => item switch
    {
        ReminderItem.Payment { AdvanceMonth: { } month } payment =>
            $"{words.Kinds[payment.PaymentKind]}, {words.AdvanceFor} {words.Months[month - 1]} {payment.Year}: {Money(payment.AmountKop)}",
        ReminderItem.Payment payment =>
            $"{words.Kinds[payment.PaymentKind]} за {Roman[payment.Quarter - 1]} {words.Quarter} {payment.Year}: {Money(payment.AmountKop)}",
        ReminderItem.Declaration declaration =>
            $"{words.DeclarationFor} {Roman[declaration.Quarter - 1]} {words.Quarter} {declaration.Year}: {words.File}",
        ReminderItem.Group3Application application => Group3ApplicationLine(application, words),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown reminder item."),
    };

    private static string Group3ApplicationLine(ReminderItem.Group3Application application, Words words)
    {
        var days = application.Deadline.DayNumber - application.RegistrationDate.DayNumber;
        return $"{words.Group3Application.FileBy} {Date(application.Deadline)} "
            + $"({days} {Plural(days, words.Days)} {words.Group3Application.FromRegistration} {Date(application.RegistrationDate)}), "
            + words.Group3Application.Otherwise;
    }

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Money(long kop) => TransactionExport.FormatScaled(kop, 2, ',', " ") + " ₴";

    // One, few and many, which Ukrainian and Russian choose by the same rule.
    private static string Plural(int count, (string One, string Few, string Many) forms) => (count % 10, count % 100) switch
    {
        (1, not 11) => forms.One,
        ( >= 2 and <= 4, not (>= 12 and <= 14)) => forms.Few,
        _ => forms.Many,
    };

    private sealed record Words(
        string Deadline,
        string In,
        (string One, string Few, string Many) Days,
        string Tomorrow,
        string Today,
        string PassedYesterday,
        Dictionary<PaymentKind, string> Kinds,
        string AdvanceFor,
        string Quarter,
        string DeclarationFor,
        string File,
        (string FileBy, string FromRegistration, string Otherwise) Group3Application,
        string[] Months,
        string Open);
}
