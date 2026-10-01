using System.Globalization;
using System.Text;

namespace TaxesUa.Api.Features.Calendar;

/// <summary>
/// The owner's deadlines as an RFC 5545 document. Kinds and periods only, never an amount: the feed's
/// URL is a bearer secret that calendar apps, and whoever shares a calendar, hold.
/// </summary>
internal static class IcsDocument
{
    public const string ContentType = "text/calendar; charset=utf-8";

    private const int MaxLineOctets = 75;

    // An all-day event starts at midnight, so the alarms count back from 00:00 to fire at 09:00 local,
    // when the Telegram reminders go out: 7 and 1 days before.
    private static readonly string[] AlarmTriggers = ["-P6DT15H", "-PT15H"];

    public static string Render(IEnumerable<CalendarDeadline> deadlines, string locale, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//taxes-ua//deadlines//" + (locale == "ru" ? "RU" : "UK"),
            "CALSCALE:GREGORIAN",
            "X-WR-CALNAME:" + Escape(CalendarTexts.CalendarName(locale)),
            "REFRESH-INTERVAL;VALUE=DURATION:PT12H",
            "X-PUBLISHED-TTL:PT12H",
        };

        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var deadline in deadlines)
        {
            var summary = CalendarTexts.Summary(deadline, locale);
            lines.Add("BEGIN:VEVENT");
            lines.Add("UID:" + deadline.Uid);
            lines.Add("DTSTAMP:" + stamp);
            lines.Add("DTSTART;VALUE=DATE:" + Day(deadline.Date));
            lines.Add("DTEND;VALUE=DATE:" + Day(deadline.Date.AddDays(1)));
            lines.Add("SUMMARY:" + Escape(summary));
            lines.Add("TRANSP:TRANSPARENT");
            foreach (var trigger in AlarmTriggers)
            {
                lines.Add("BEGIN:VALARM");
                lines.Add("ACTION:DISPLAY");
                lines.Add("DESCRIPTION:" + Escape(summary));
                lines.Add("TRIGGER:" + trigger);
                lines.Add("END:VALARM");
            }

            lines.Add("END:VEVENT");
        }

        lines.Add("END:VCALENDAR");

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            Fold(builder, line);
        }

        return builder.ToString();
    }

    private static string Day(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    // TEXT values escape the backslash first, then the separators and line breaks (RFC 5545 3.3.11).
    internal static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    // A content line is at most 75 octets before its CRLF; the rest continues on lines that start with
    // one space, which counts toward the 75. A line is cut between characters, never inside a
    // multi-byte one.
    internal static void Fold(StringBuilder builder, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var width = rune.Utf8SequenceLength;
            if (octets + width > MaxLineOctets)
            {
                builder.Append("\r\n ");
                octets = 1;
            }

            builder.Append(rune.ToString());
            octets += width;
        }

        builder.Append("\r\n");
    }
}

internal static class CalendarTexts
{
    private static readonly string[] UkMonths =
    [
        "січень", "лютий", "березень", "квітень", "травень", "червень",
        "липень", "серпень", "вересень", "жовтень", "листопад", "грудень",
    ];

    private static readonly string[] RuMonths =
    [
        "январь", "февраль", "март", "апрель", "май", "июнь",
        "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь",
    ];

    public static string CalendarName(string locale) => locale == "ru" ? "Налоги ФЛП: сроки" : "Податки ФОП: терміни";

    public static string Summary(CalendarDeadline deadline, string locale)
    {
        var ru = locale == "ru";
        return deadline.Kind switch
        {
            CalendarDeadlineKind.Esv => $"{(ru ? "Уплата ЕСВ" : "Сплата ЄСВ")} · Q{deadline.Quarter} {deadline.Year}",
            CalendarDeadlineKind.TaxPayment => $"{(ru ? "Уплата ЕН и ВС" : "Сплата ЄП і ВЗ")} · Q{deadline.Quarter} {deadline.Year}",
            CalendarDeadlineKind.Declaration => $"{(ru ? "Подача декларации" : "Подання декларації")} · Q{deadline.Quarter} {deadline.Year}",
            CalendarDeadlineKind.Advance =>
                $"{(ru ? "Аванс ЕН, ВС и ЕСВ" : "Аванс ЄП, ВЗ і ЄСВ")} · {(ru ? RuMonths : UkMonths)[deadline.Month!.Value - 1]} {deadline.Year}",
            _ => throw new ArgumentOutOfRangeException(nameof(deadline), deadline.Kind, message: null),
        };
    }
}
