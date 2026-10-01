using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;
using PaymentMode = TaxesUa.Api.Features.Settings.PaymentMode;

namespace TaxesUa.Api.Tests.Features.Notifications;

// The owner registered on 2031-01-01, is exempt from ESV and received 123,456.00 in February, so the first
// quarter owes 6,172.80 single tax and 1,234.56 military levy, both due Tuesday 2031-05-20, and its
// declaration is due Monday 2031-05-12 (the 10th is a Saturday).
public sealed partial class ReminderTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const long SingleTaxKop = 617_280;

    private const long MilitaryLevyKop = 123_456;

    private static readonly DateOnly TaxDue = new(2031, 5, 20);

    private const string WeekBeforeText =
        "Податки: строк 20.05.2031, через 7 днів.\n"
        + "Єдиний податок за I квартал 2031: 6 172,80 ₴\n"
        + "Військовий збір за I квартал 2031: 1 234,56 ₴";

    [Fact]
    public async Task A_reminder_goes_to_the_channel_once_however_many_runs_see_it()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0));
        await using var application = fixture.CreateApplication(
            telegram, clock, configure: builder => builder.UseSetting("App:PublicUrl", "https://taxes.example.com"));
        await Prepare(application, telegram);

        await Run(application);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Run(application);

        var sent = Assert.Single(telegram.To("sendMessage"));
        Assert.Equal(OwnerChat.ToString(), sent.Body["chat_id"]!.GetValue<string>());
        Assert.Equal(WeekBeforeText + "\nВідкрити застосунок: https://taxes.example.com/", sent.Body["text"]!.GetValue<string>());
        var claim = Assert.Single(await SentLog(application));
        Assert.Equal((TaxDue, ReminderKinds.SingleTax | ReminderKinds.MilitaryLevy, ReminderOffset.WeekBefore), (claim.Date, claim.Kinds, claim.Offset));
        Assert.NotNull(claim.DeliveredAt);
    }

    [Fact]
    public async Task Two_runs_at_once_send_one_message()
    {
        var telegram = new StubTelegramHandler();
        await using var application = fixture.CreateApplication(telegram, new FakeTimeProvider(Kyiv(TaxDue.AddDays(-7), 9, 0)));
        await Prepare(application, telegram);

        await Task.WhenAll(Run(application), Run(application), Run(application));

        Assert.Equal([WeekBeforeText], Texts(telegram));
    }

    [Fact]
    public async Task A_restart_in_the_middle_of_a_send_does_not_send_it_again()
    {
        var telegram = new StubTelegramHandler();
        var moment = Kyiv(TaxDue.AddDays(-7), 9, 0);
        using var stop = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        await using (var application = fixture.CreateApplication(telegram, new FakeTimeProvider(moment)))
        {
            await Prepare(application, telegram);
            telegram.Override = call =>
            {
                if (call.Method != "sendMessage")
                {
                    return null;
                }

                reached.TrySetResult();
                stop.Token.WaitHandle.WaitOne();
                stop.Token.ThrowIfCancellationRequested();
                return null;
            };

            var running = application.Services.GetRequiredService<ReminderSender>().RunOnceAsync(stop.Token);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await stop.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }

        var afterRestart = new StubTelegramHandler();
        await using var restarted = fixture.CreateApplication(afterRestart, new FakeTimeProvider(moment + TimeSpan.FromMinutes(5)));
        await Run(restarted);

        Assert.Empty(afterRestart.To("sendMessage"));
        Assert.Null(Assert.Single(await SentLog(restarted)).DeliveredAt);
    }

    [Fact]
    public async Task A_server_back_from_downtime_sends_the_latest_reminder_once_and_not_an_old_one()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-3), 10, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        await Prepare(application, telegram);

        await Run(application);
        clock.SetUtcNow(Kyiv(TaxDue.AddDays(-3), 18, 0));
        await Run(application);

        Assert.Equal([WeekBeforeText.Replace("через 7 днів", "через 3 дні")], Texts(telegram));

        telegram.ClearCalls();
        clock.SetUtcNow(Kyiv(TaxDue.AddDays(2), 10, 0));
        await Run(application);
        Assert.Empty(Texts(telegram));
    }

    [Fact]
    public async Task The_day_before_the_day_itself_and_the_day_after_each_send_once_and_then_nothing()
    {
        var telegram = new StubTelegramHandler();
        var clock = new FakeTimeProvider(Kyiv(TaxDue.AddDays(-1), 9, 0));
        await using var application = fixture.CreateApplication(telegram, clock);
        await Prepare(application, telegram);
        var items = WeekBeforeText[WeekBeforeText.IndexOf('\n')..];

        await Run(application);
        clock.SetUtcNow(Kyiv(TaxDue, 9, 0));
        await Run(application);
        clock.SetUtcNow(Kyiv(TaxDue.AddDays(1), 8, 59));
        await Run(application);
        Assert.Equal(2, Texts(telegram).Count);

        clock.SetUtcNow(Kyiv(TaxDue.AddDays(1), 9, 0));
        await Run(application);
        clock.SetUtcNow(Kyiv(TaxDue.AddDays(1), 15, 0));
        await Run(application);
        clock.SetUtcNow(Kyiv(TaxDue.AddDays(2), 9, 0));
        await Run(application);

        Assert.Equal(
            [
                "Податки: строк завтра, 20.05.2031." + items,
                "Податки: строк сьогодні, 20.05.2031." + items,
                "Податки: строк минув учора, 20.05.2031." + items,
            ],
            Texts(telegram));
    }

    private static DateTimeOffset Kyiv(DateOnly date, int hour, int minute) => date.InKyiv(new TimeOnly(hour, minute));

    private static Task Run(WebApplicationFactory<Program> application) =>
        application.Services.GetRequiredService<ReminderSender>().RunOnceAsync(CancellationToken.None);

    private static List<string> Texts(StubTelegramHandler telegram) =>
        [.. telegram.To("sendMessage").Select(call => call.Body["text"]!.GetValue<string>())];

    // Runs the sender while stepping the fake clock, so TelegramDelivery's backoff between attempts passes.
    private static async Task RunThroughBackoff(WebApplicationFactory<Program> application, FakeTimeProvider clock)
    {
        var running = Run(application);
        while (!running.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        await running;
    }

    // The database is shared by the class, so every test starts from the same owner: nothing sent,
    // paid, filed or linked, one income receipt, and Telegram linked. The linking reply is not counted.
    private static async Task<HttpClient> Prepare(
        WebApplicationFactory<Program> application,
        StubTelegramHandler telegram,
        string locale = "uk",
        PaymentMode mode = PaymentMode.Quarterly)
    {
        var owner = await PrepareOwner(application, locale, mode);
        await Link(application, owner, telegram);
        telegram.ClearCalls();
        return owner;
    }

    // The same owner with no channel connected, for the email channel's tests.
    private static async Task<HttpClient> PrepareOwner(
        WebApplicationFactory<Program> application, string locale = "uk", PaymentMode mode = PaymentMode.Quarterly)
    {
        var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await OwnerId(database);
            await database.SentReminders.Where(row => row.UserId == userId).ExecuteDeleteAsync();
            await database.BudgetPayments.Where(row => row.UserId == userId).ExecuteDeleteAsync();
            await database.DeclarationFilings.Where(row => row.UserId == userId).ExecuteDeleteAsync();
            await database.Transactions.Where(row => row.UserId == userId).ExecuteDeleteAsync();
            await Reset(scope.ServiceProvider);
        }

        var taxYear = new TaxYearConfigRequest(800_000, 500, 100, 2_200, 1_500, 1_000, [85], 19, 40, 10, 15, [], "a test source");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/tax-years/2031", taxYear, Json)).StatusCode);
        await SetSettings(owner, new DateOnly(2031, 1, 1), locale, mode, esvExempt: true);
        var income = new TransactionRequest(
            new DateOnly(2031, 2, 10), 12_345_600, Currency.UAH, null, TransactionKind.Income, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", income, Json)).StatusCode);

        return owner;
    }

    private static async Task Pay(HttpClient owner, PaymentKind kind, long amountKop, DateOnly paidOn)
    {
        var payment = new PaymentRequest(paidOn, kind, amountKop, 2031, 1, null, null);
        var response = await owner.PostAsJsonAsync("/api/payments", payment, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task MarkFiled(HttpClient owner, DateOnly filedOn)
    {
        var response = await owner.PutAsJsonAsync(
            "/api/declarations/2031/1/filing", new DeclarationFilingRequest(filedOn, DeclarationType.Reporting), Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<SentReminder>> SentLog(WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await OwnerId(database);
        return await database.SentReminders.AsNoTracking().Where(row => row.UserId == userId).ToListAsync();
    }

    private static Task<string> OwnerId(AppDbContext database) =>
        database.Users.Where(user => user.Email == ApiFixture.AllowedEmail).Select(user => user.Id).SingleAsync();
}
