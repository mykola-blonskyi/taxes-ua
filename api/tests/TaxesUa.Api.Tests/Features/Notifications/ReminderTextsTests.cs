using TaxesUa.Api.Features.Notifications;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class ReminderTextsTests
{
    private static readonly DateOnly Date = new(2031, 10, 20);

    [Theory]
    [InlineData(2, "uk", "через 2 дні")]
    [InlineData(5, "uk", "через 5 днів")]
    [InlineData(11, "uk", "через 11 днів")]
    [InlineData(14, "uk", "через 14 днів")]
    [InlineData(21, "uk", "через 21 день")]
    [InlineData(22, "uk", "через 22 дні")]
    [InlineData(2, "ru", "через 2 дня")]
    [InlineData(7, "ru", "через 7 дней")]
    [InlineData(21, "ru", "через 21 день")]
    [InlineData(12, "ru", "через 12 дней")]
    public void The_days_left_take_the_plural_the_number_needs(int days, string locale, string expected)
    {
        var message = ReminderTexts.Render(Reminder(new ReminderItem.Declaration(2031, 3)), locale, Date.AddDays(-days), null);

        Assert.EndsWith($", {expected}.", message.Subject);
    }

    [Theory]
    [InlineData("uk", "Податки: строк минув учора, 20.10.2031.", "ЄСВ за III квартал 2031: 1 234 567,05 ₴", "Відкрити застосунок: https://taxes.example.com/")]
    [InlineData("ru", "Налоги: срок истёк вчера, 20.10.2031.", "ЕСВ за III квартал 2031: 1 234 567,05 ₴", "Открыть приложение: https://taxes.example.com/")]
    public void The_subject_is_the_first_line_and_the_link_the_last(string locale, string subject, string item, string link)
    {
        var message = ReminderTexts.Render(
            Reminder(new ReminderItem.Payment(PaymentKind.Esv, 2031, 3, null, 123_456_705)),
            locale,
            Date.AddDays(1),
            "https://taxes.example.com/");

        Assert.Equal(subject, message.Subject);
        Assert.Equal(string.Join('\n', subject, item, link), message.Text);
    }

    [Fact]
    public void Any_locale_but_russian_reads_in_ukrainian()
    {
        var message = ReminderTexts.Render(
            Reminder(new ReminderItem.Payment(PaymentKind.Esv, 2031, 3, 7, 5)), "en", Date.AddDays(-1), null);

        Assert.Equal("Податки: строк завтра, 20.10.2031.\nЄСВ, аванс за липень 2031: 0,05 ₴", message.Text);
    }

    private static Reminder Reminder(ReminderItem item) => new(Date, ReminderOffset.WeekBefore, [item]);
}
