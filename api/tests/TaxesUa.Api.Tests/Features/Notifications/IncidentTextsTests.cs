using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class IncidentTextsTests
{
    private static readonly DateTimeOffset Since = new(2031, 5, 3, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_stale_sync_names_the_last_success_by_Kyiv_date_and_links_to_the_monobank_tab()
    {
        var incident = new Incident("SyncStale:1", IncidentKind.SyncStale, Since);

        var uk = IncidentTexts.Render(incident, "uk", "https://taxes.test/");
        var ru = IncidentTexts.Render(incident, "ru", "https://taxes.test/");

        Assert.Equal("Monobank: синхронізація не працює, оновлень немає з 04.05.2031.", uk.Subject);
        Assert.Equal(
            uk.Subject + "\nДоходи в застосунку можуть бути неповними. Перевірте підключення monobank."
            + "\nВідкрити налаштування: https://taxes.test/settings?tab=monobank",
            uk.Text);
        Assert.Equal("Monobank: синхронизация не работает, обновлений нет с 04.05.2031.", ru.Subject);
        Assert.EndsWith("\nОткрыть настройки: https://taxes.test/settings?tab=monobank", ru.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TokenRejected", "uk", "Monobank відхилив токен, синхронізацію зупинено.")]
    [InlineData("TokenRejected", "ru", "Monobank отклонил токен, синхронизация остановлена.")]
    [InlineData("TokenUnreadable", "uk", "Токен monobank не вдалося прочитати, синхронізацію зупинено.")]
    [InlineData("TokenUnreadable", "ru", "Токен monobank не удалось прочитать, синхронизация остановлена.")]
    public void A_token_incident_is_worded_in_the_owners_language_and_goes_without_a_link_when_the_address_is_unknown(
        string kind, string locale, string subject)
    {
        var message = IncidentTexts.Render(new Incident("k", Enum.Parse<IncidentKind>(kind), null), locale, null);

        Assert.Equal(subject, message.Subject);
        Assert.Equal(2, message.Text.Split('\n').Length);
        Assert.DoesNotContain("http", message.Text, StringComparison.Ordinal);
    }
}
