using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;
using static TaxesUa.Api.Tests.Features.Notifications.EmailChannelTests;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;

namespace TaxesUa.Api.Tests.Features.Notifications;

// Registered on Sunday 2031-05-11 instead, the owner has until Wednesday 2031-05-21 to apply for group 3
// (Tax Code 298.1.2). The February receipt predates registration, so nothing else is due around then.
public sealed partial class ReminderTests
{
    private static readonly DateOnly RegisteredInMay = new(2031, 5, 11);

    private static readonly DateOnly ApplicationDue = new(2031, 5, 21);

    private const string ApplicationLine =
        "Заява про обрання 3 групи єдиного податку: подати до 21.05.2031 (10 днів від реєстрації 11.05.2031), "
        + "інакше 3 група почнеться не раніше наступного кварталу";

    [Fact]
    public async Task The_group_3_application_is_reminded_through_telegram_until_confirmed()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(ApplicationDue.AddDays(-1), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);
        await RegisterInMay(owner, "uk");

        await Run(application);
        await PutDpsStatus(owner, new Group3ConfirmationDto(new DateOnly(2031, 5, 20), "9123"));
        clock.SetUtcNow(Kyiv(ApplicationDue, 9, 0));
        await Run(application);
        await PutDpsStatus(owner, null);

        Assert.Equal(["Податки: строк завтра, 21.05.2031.\n" + ApplicationLine], Texts(telegram));
        var claim = Assert.Single(await SentLog(application));
        Assert.Equal((ApplicationDue, ReminderKinds.Group3Application, ReminderOffset.DayBefore), (claim.Date, claim.Kinds, claim.Offset));
    }

    [Fact]
    public async Task The_group_3_application_is_reminded_by_email()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Kyiv(ApplicationDue, 9, 0)));
        var owner = await PrepareOwner(application);
        await Connected(owner, email, MailAddress);
        await RegisterInMay(owner, "uk");

        await Run(application);

        var sent = Assert.Single(email.Delivered);
        Assert.Equal("Податки: строк сьогодні, 21.05.2031.", sent.Subject);
        Assert.Equal(
            "Податки: строк сьогодні, 21.05.2031.\n" + ApplicationLine + "\nВідкрити застосунок: https://taxes.test/",
            sent.Text);
    }

    [Fact]
    public async Task The_group_3_application_reminder_is_in_russian_for_a_russian_owner()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(ApplicationDue.AddDays(-7), 9, 0)));
        var owner = await Prepare(application, telegram, locale: "ru");
        await RegisterInMay(owner, "ru");

        await Run(application);

        Assert.Equal(
            [
                "Налоги: срок 21.05.2031, через 7 дней.\n"
                + "Заявление о выборе 3 группы единого налога: подать до 21.05.2031 (10 дней от регистрации 11.05.2031), "
                + "иначе 3 группа начнётся не раньше следующего квартала",
            ],
            Texts(telegram));
    }

    private static async Task RegisterInMay(HttpClient owner, string locale)
    {
        await SetSettings(owner, RegisteredInMay, locale, esvExempt: true);
        await PutDpsStatus(owner, null);
    }

    private static async Task PutDpsStatus(HttpClient owner, Group3ConfirmationDto? confirmation)
    {
        var response = await owner.PutAsJsonAsync(
            "/api/settings/dps-status", new DpsStatusRequest(null, confirmation, false, false, false), Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
