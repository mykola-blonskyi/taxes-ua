using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.DatabaseBackups;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Tests.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.DatabaseBackups;

// A restore check that fails or goes missing is told to the owner once per incident (Rule 18). The sender
// is driven by hand with a fake clock; the sidecar's rows are written straight into the table it owns.
public sealed class RestoreCheckAlertTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Advice = "Відновити базу з копій може бути неможливо. Перегляньте журнал сервісу backup у Coolify.";

    private const string NeverText =
        "Резервні копії: перевірка відновлення ще жодного разу не пройшла.\n" + Advice;

    private const string FailingText =
        "Резервні копії: перевірка відновлення не проходить, остання успішна {0}.\n" + Advice;

    private static readonly DateTimeOffset Start = new(2081, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly ApiFixture _fixture = fixture;

    private readonly string _ownerEmail = fixture.NewOwner();

    // Every sender pass walks every owner in the class's database, so an earlier test's owner is alerted
    // through this test's stub too. Each test links a chat of its own and reads only what went there.
    private readonly long _chat = Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);

    [Fact]
    public async Task Nothing_is_sent_while_no_run_has_been_recorded()
    {
        await using var scenario = await Scenario.Open(this);

        await scenario.Send();

        Assert.Empty(scenario.Alerts());
        Assert.Empty(await scenario.Claims());
    }

    [Fact]
    public async Task A_failed_check_alerts_once_a_later_success_clears_it_and_a_later_failure_alerts_again()
    {
        await using var scenario = await Scenario.Open(this);
        var failedAt = Start.AddDays(-1);
        await scenario.Record(DatabaseBackupJob.RestoreCheck, failedAt, succeeded: false);

        await scenario.Send();
        await scenario.Send();

        Assert.Equal([NeverText], scenario.Alerts());
        var first = Assert.Single(await scenario.Claims());
        Assert.Equal($"RestoreCheckFailed:{failedAt.ToUnixTimeSeconds()}", first.Incident);
        Assert.NotNull(first.DeliveredAt);

        var passedAt = Start.AddDays(6);
        scenario.Clock.SetUtcNow(passedAt.AddHours(1));
        await scenario.Record(DatabaseBackupJob.RestoreCheck, passedAt, succeeded: true);
        await scenario.Send();
        Assert.Single(scenario.Alerts());

        scenario.Clock.SetUtcNow(passedAt.AddDays(7).AddHours(1));
        await scenario.Record(DatabaseBackupJob.RestoreCheck, passedAt.AddDays(7), succeeded: false);
        await scenario.Send();

        Assert.Equal([NeverText, string.Format(FailingText, "07.03.2081")], scenario.Alerts());
        Assert.Equal(
            [first.Incident, $"RestoreCheckFailed:{passedAt.ToUnixTimeSeconds()}"],
            (await scenario.Claims()).Select(claim => claim.Incident));
    }

    [Fact]
    public async Task A_check_that_stops_running_alerts_once_after_eight_days_and_a_failure_after_that_adds_nothing()
    {
        await using var scenario = await Scenario.Open(this);
        await scenario.Record(DatabaseBackupJob.RestoreCheck, Start, succeeded: true);

        scenario.Clock.SetUtcNow(Start.AddDays(7));
        await scenario.Send();
        Assert.Empty(scenario.Alerts());

        scenario.Clock.SetUtcNow(Start.AddDays(8).AddHours(1));
        await scenario.Send();
        await scenario.Send();
        Assert.Equal([string.Format(FailingText, "01.03.2081")], scenario.Alerts());

        await scenario.Record(DatabaseBackupJob.RestoreCheck, Start.AddDays(8).AddHours(2), succeeded: false);
        scenario.Clock.SetUtcNow(Start.AddDays(9));
        await scenario.Send();

        Assert.Single(scenario.Alerts());
        var claim = Assert.Single(await scenario.Claims());
        Assert.Equal($"RestoreCheckFailed:{Start.ToUnixTimeSeconds()}", claim.Incident);
    }

    [Fact]
    public async Task Backups_without_any_restore_check_alert_after_eight_days_from_the_oldest_run()
    {
        await using var scenario = await Scenario.Open(this);
        await scenario.Record(DatabaseBackupJob.Backup, Start, succeeded: true);
        await scenario.Record(DatabaseBackupJob.Backup, Start.AddDays(1), succeeded: true);

        scenario.Clock.SetUtcNow(Start.AddDays(8));
        await scenario.Send();
        Assert.Empty(scenario.Alerts());

        scenario.Clock.SetUtcNow(Start.AddDays(8).AddHours(1));
        await scenario.Send();
        await scenario.Send();

        Assert.Equal([NeverText], scenario.Alerts());
        var claim = Assert.Single(await scenario.Claims());
        Assert.Equal($"RestoreCheckFailed:{Start.ToUnixTimeSeconds()}", claim.Incident);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly RestoreCheckAlertTests _test;

        private readonly StubTelegramHandler _telegram;

        private readonly WebApplicationFactory<Program> _app;

        private Scenario(RestoreCheckAlertTests test, StubTelegramHandler telegram, FakeTimeProvider clock, WebApplicationFactory<Program> app)
        {
            _test = test;
            _telegram = telegram;
            Clock = clock;
            _app = app;
        }

        public FakeTimeProvider Clock { get; }

        public static async Task<Scenario> Open(RestoreCheckAlertTests test)
        {
            var telegram = new StubTelegramHandler();
            var clock = new FakeTimeProvider(Start);
            var app = test._fixture.CreateApplication(telegram, clock);
            var scenario = new Scenario(test, telegram, clock, app);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().DatabaseBackupRuns.ExecuteDeleteAsync();
            }

            using var owner = await ApiFixture.SignIn(app, test._ownerEmail);
            await TelegramSteps.ForgetPollOffset(app);
            var next = telegram.Updates.Count == 0 ? 10 : telegram.Updates.Max(update => update["update_id"]!.GetValue<long>()) + 1;
            telegram.Updates.Add(StubTelegramHandler.Update(next, test._chat, $"/start {TelegramSteps.CodeOf(await TelegramSteps.Connect(owner))}"));
            await TelegramSteps.Poll(app);
            Assert.True((await TelegramSteps.Channel(owner))["linked"]!.GetValue<bool>());
            telegram.ClearCalls();
            return scenario;
        }

        public async Task Record(DatabaseBackupJob job, DateTimeOffset finishedAt, bool succeeded)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.DatabaseBackupRuns.Add(new DatabaseBackupRun
            {
                Job = job,
                FinishedAt = finishedAt,
                Succeeded = succeeded,
                Detail = succeeded ? "ok" : "restore failed",
            });
            await database.SaveChangesAsync();
        }

        public Task Send() => _app.Services.GetRequiredService<ReminderSender>().RunOnceAsync(CancellationToken.None);

        public List<string> Alerts() =>
        [
            .. _telegram.To("sendMessage")
                .Where(call => call.Body["chat_id"]!.GetValue<string>() == _test._chat.ToString(CultureInfo.InvariantCulture))
                .Select(call => call.Body["text"]!.GetValue<string>()),
        ];

        public async Task<List<SentReminder>> Claims()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = database.Users.Where(user => user.Email == _test._ownerEmail).Select(user => user.Id);
            return await database.SentReminders.AsNoTracking()
                .Where(row => owner.Contains(row.UserId) && row.Incident != string.Empty)
                .OrderBy(row => row.ClaimedAt)
                .ToListAsync();
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();
    }
}
