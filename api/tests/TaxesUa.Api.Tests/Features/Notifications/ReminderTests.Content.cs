using Microsoft.Extensions.Time.Testing;
using TaxesUa.Engine;
using PaymentMode = TaxesUa.Api.Features.Settings.PaymentMode;

namespace TaxesUa.Api.Tests.Features.Notifications;

// What a reminder says follows the ledger at the moment it is sent.
public sealed partial class ReminderTests
{
    [Fact]
    public async Task Nothing_is_sent_for_a_quarter_paid_in_full()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0)));
        var owner = await Prepare(application, telegram);
        await Pay(owner, PaymentKind.SingleTax, SingleTaxKop, TaxDue.AddDays(-7));
        await Pay(owner, PaymentKind.MilitaryLevy, MilitaryLevyKop, TaxDue.AddDays(-7));

        await Run(application);

        Assert.Empty(Texts(telegram));
        Assert.Empty(await SentLog(application));
    }

    [Fact]
    public async Task A_part_payment_leaves_the_remainder_in_the_reminder()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0)));
        var owner = await Prepare(application, telegram);
        await Pay(owner, PaymentKind.SingleTax, 17_280, TaxDue.AddDays(-7));

        await Run(application);

        Assert.Equal(
            [
                "Податки: строк 20.05.2031, через 7 днів.\n"
                + "Єдиний податок за I квартал 2031: 6 000,00 ₴\n"
                + "Військовий збір за I квартал 2031: 1 234,56 ₴",
            ],
            Texts(telegram));
    }

    [Fact]
    public async Task Paying_one_kind_after_a_reminder_does_not_send_that_reminder_again()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);

        await Run(application);
        await Pay(owner, PaymentKind.MilitaryLevy, MilitaryLevyKop, TaxDue.AddDays(-7));
        clock.Advance(TimeSpan.FromHours(3));
        await Run(application);

        Assert.Equal([WeekBeforeText], Texts(telegram));
    }

    [Fact]
    public async Task A_declaration_not_marked_filed_has_its_own_reminder_and_marking_it_stops_them()
    {
        var telegram = new StubTelegramHandler();
        var declarationDue = new DateOnly(2031, 5, 12);
        var clock = new FakeTimeProvider(Kyiv(declarationDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        var owner = await Prepare(application, telegram);

        await Run(application);
        await MarkFiled(owner, declarationDue.AddDays(-7));
        clock.SetUtcNow(Kyiv(declarationDue.AddDays(-1), 9, 0));
        await Run(application);

        Assert.Equal(["Податки: строк 12.05.2031, через 7 днів.\nДекларація за I квартал 2031: подати"], Texts(telegram));
    }

    [Fact]
    public async Task In_monthly_advance_mode_an_advance_is_reminded_on_its_recommended_date()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(new DateOnly(2031, 3, 15), 9, 0)));
        await Prepare(application, telegram, mode: PaymentMode.MonthlyAdvance);

        await Run(application);

        Assert.Equal(
            [
                "Податки: строк сьогодні, 15.03.2031.\n"
                + "Єдиний податок, аванс за лютий 2031: 6 172,80 ₴\n"
                + "Військовий збір, аванс за лютий 2031: 1 234,56 ₴",
            ],
            Texts(telegram));
    }

    [Fact]
    public async Task An_owner_who_reads_the_app_in_russian_gets_the_reminder_in_russian()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0)));
        await Prepare(application, telegram, locale: "ru");

        await Run(application);

        Assert.Equal(
            [
                "Налоги: срок 20.05.2031, через 7 дней.\n"
                + "Единый налог за I квартал 2031: 6 172,80 ₴\n"
                + "Военный сбор за I квартал 2031: 1 234,56 ₴",
            ],
            Texts(telegram));
    }
}
