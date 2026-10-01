using System.Text;
using IcsCalendar = Ical.Net.Calendar;
using TaxesUa.Api.Features.Calendar;

namespace TaxesUa.Api.Tests.Features.Calendar;

public sealed class IcsDocumentTests
{
    [Theory]
    [InlineData("plain")]
    [InlineData("commas, semicolons; backslash \\ and a\nnew line")]
    [InlineData("Сплата ЄСВ, ЄП і ВЗ; аванс за березень — рядок, що не вміщується в сімдесят п'ять октетів і мусить бути згорнутий")]
    [InlineData("ascii only but long enough to be folded at least once, because it keeps going past seventy five octets")]
    public void A_text_value_survives_escaping_and_folding_through_a_parser(string summary)
    {
        var document = new StringBuilder();
        foreach (var line in new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//EN", "BEGIN:VEVENT", "UID:one@test",
            "DTSTAMP:20900101T000000Z", "DTSTART;VALUE=DATE:20900102", "SUMMARY:" + IcsDocument.Escape(summary),
            "END:VEVENT", "END:VCALENDAR",
        })
        {
            IcsDocument.Fold(document, line);
        }

        var calendar = IcsCalendar.Load(document.ToString())!;

        Assert.Equal(summary, calendar.Events.Single().Summary);
    }

    [Fact]
    public void Every_physical_line_is_at_most_75_octets_and_no_character_is_split()
    {
        var builder = new StringBuilder();

        IcsDocument.Fold(builder, "SUMMARY:" + string.Concat(Enumerable.Repeat("Сплата ЄСВ 🧾 ", 20)));

        var lines = builder.ToString().Split("\r\n", StringSplitOptions.None);
        Assert.Equal(string.Empty, lines[^1]);
        Assert.True(lines.Length > 3);
        Assert.All(lines[..^1], line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.All(lines[1..^1], line => Assert.StartsWith(" ", line));
        Assert.DoesNotContain('�', builder.ToString());
    }

    [Fact]
    public void The_document_uses_crlf_throughout_and_ends_with_a_line_break()
    {
        var ics = IcsDocument.Render(
            [new CalendarDeadline("3f9a1c2e", CalendarDeadlineKind.Esv, 2090, 1, null, new DateOnly(2090, 4, 19))],
            "uk",
            new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", string.Empty));
        Assert.Contains("DTSTAMP:20900101T000000Z\r\n", ics);
    }
}
