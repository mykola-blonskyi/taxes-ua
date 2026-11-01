using System.Globalization;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The keep-until line in the words of the periods screen (messages <c>periods.keepUntil</c>), so the ZIP's
/// table of contents, the archive reminder and the screen cannot tell the owner two things. Anything but
/// "ru" is Ukrainian.
/// </summary>
internal static class KeepUntilWording
{
    private static readonly Words Ukrainian = new(
        "Документи за {0} рік зберігати до {1}",
        "Документи за {0} рік зберігати не менше ніж до {1}. Строк продовжено на час воєнного стану: після його припинення або скасування ще {2} {3}.",
        "Документи за {0} рік: строк зберігання не визначено, бо за цей рік немає декларації платника третьої групи або не вказано дату реєстрації ФОП.",
        ("день", "дні", "днів"));

    private static readonly Words Russian = new(
        "Документы за {0} год хранить до {1}",
        "Документы за {0} год хранить не менее чем до {1}. Срок продлён на время военного положения: после его прекращения или отмены ещё {2} {3}.",
        "Документы за {0} год: срок хранения не определён, потому что за этот год нет декларации плательщика третьей группы или не указана дата регистрации ФОП.",
        ("день", "дня", "дней"));

    public static string Line(string locale, int year, KeepUntilResponse? keepUntil)
    {
        var words = locale == "ru" ? Russian : Ukrainian;
        return keepUntil switch
        {
            null => Format(words.None, year),
            { State: KeepUntilState.ExtendedWhileSuspended } open => Format(
                words.Extended,
                year,
                Date(open.Date),
                open.DaysAfterSuspension ?? 0,
                Plural(open.DaysAfterSuspension ?? 0, words.Days)),
            _ => Format(words.Fixed, year, Date(keepUntil.Date)),
        };
    }

    private static string Format(string template, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, template, arguments);

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    // One, few and many, which Ukrainian and Russian choose by the same rule.
    private static string Plural(int count, (string One, string Few, string Many) forms) => (count % 10, count % 100) switch
    {
        (1, not 11) => forms.One,
        ( >= 2 and <= 4, not (>= 12 and <= 14)) => forms.Few,
        _ => forms.Many,
    };

    private sealed record Words(string Fixed, string Extended, string None, (string One, string Few, string Many) Days);
}
