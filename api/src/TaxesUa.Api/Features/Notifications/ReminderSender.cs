using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// One pass over every owner with a switched-on channel: what <see cref="ReminderPlan"/> says is due
/// at this Kyiv moment goes to each such channel once. Each send is claimed in
/// <see cref="SentReminder"/> first. A failure that may pass (Telegram down, a 429, the channel
/// switched off meanwhile) gives the claim back so a later pass tries again within the window; a
/// failure that will not pass (blocked, rejected) keeps it, and so does a pass cut off mid-send.
/// </summary>
internal sealed class ReminderSender(
    IServiceScopeFactory scopes,
    AppLink link,
    TimeProvider time,
    ILogger<ReminderSender> logger)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var today = time.TodayInKyiv();
        var now = TimeOnly.FromDateTime(time.NowInKyiv());

        List<string> owners;
        await using (var scope = scopes.CreateAsyncScope())
        {
            NotificationChannelKind[] available =
            [
                .. scope.ServiceProvider.GetServices<IReminderChannel>()
                    .Where(channel => channel.IsAvailable)
                    .Select(channel => channel.Kind),
            ];
            if (available.Length == 0)
            {
                return;
            }

            owners = await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationChannels
                .Where(row => row.Enabled && row.ConfirmedAt != null && available.Contains(row.Kind))
                .Select(row => row.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        foreach (var userId in owners)
        {
            try
            {
                await RunForOwnerAsync(userId, today, now, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Reminders for owner {UserId} could not be sent.", userId);
            }
        }
    }

    private async Task RunForOwnerAsync(string userId, DateOnly today, TimeOnly now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // In January the new year may not be configured yet, and last year's fourth quarter is still due.
        var loaded = await YearAccruals.LoadAsync(database, userId, today.Year, cancellationToken)
            ?? await YearAccruals.LoadAsync(database, userId, today.Year - 1, cancellationToken);
        if (loaded is null
            || await loaded.PaymentLedgerAsync(database, userId, today, cancellationToken) is not { } ledger)
        {
            return;
        }

        var filed = (await database.DeclarationFilings
                .Where(row => row.UserId == userId)
                .Select(row => new { row.Year, row.Quarter })
                .ToListAsync(cancellationToken))
            .Select(row => new YearQuarter(row.Year, row.Quarter))
            .ToHashSet();
        var settings = loaded.Viewed.Settings;
        var reminders = ReminderPlan.Due(
            loaded.LedgerYears, settings.ToEngineInput(), ledger, loaded.AdvancesOf(ledger), filed, today, now);
        if (reminders.Count == 0)
        {
            return;
        }

        var enabled = await database.NotificationChannels
            .Where(row => row.UserId == userId && row.Enabled && row.ConfirmedAt != null)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);
        var channels = scope.ServiceProvider.GetServices<IReminderChannel>()
            .Where(channel => channel.IsAvailable && enabled.Contains(channel.Kind))
            .ToArray();

        foreach (var reminder in reminders)
        {
            var message = ReminderTexts.Render(reminder, settings.Locale, today, link.Url);
            foreach (var channel in channels)
            {
                await SendAsync(database, channel, userId, reminder, message, cancellationToken);
            }
        }
    }

    private async Task SendAsync(
        AppDbContext database,
        IReminderChannel channel,
        string userId,
        Reminder reminder,
        ReminderMessage message,
        CancellationToken cancellationToken)
    {
        // A kind paid off since an earlier message leaves a smaller set that is already covered; a kind
        // that became owed since is not, and is worth a message of its own.
        var covered = (await database.SentReminders
                .Where(row => row.UserId == userId
                    && row.Date == reminder.Date
                    && row.Offset == reminder.Offset
                    && row.Channel == channel.Kind)
                .Select(row => row.Kinds)
                .ToListAsync(cancellationToken))
            .Aggregate(ReminderKinds.None, (all, kinds) => all | kinds);
        if ((reminder.Kinds & ~covered) == ReminderKinds.None)
        {
            return;
        }

        var claim = new SentReminder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = reminder.Date,
            Kinds = reminder.Kinds,
            Offset = reminder.Offset,
            Channel = channel.Kind,
            ClaimedAt = time.GetUtcNow(),
        };
        database.SentReminders.Add(claim);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another pass claimed it first. Left tracked, the claim would be inserted again by the
            // next save on this context, which the channel's delivery shares.
            database.Entry(claim).State = EntityState.Detached;
            return;
        }

        var result = await channel.SendAsync(userId, message, cancellationToken);
        if (result.Outcome == DeliveryOutcome.Sent)
        {
            claim.DeliveredAt = time.GetUtcNow();
        }
        else if (result.Failure is not { } failure || failure.IsTransient())
        {
            logger.LogInformation(
                "A {Channel} reminder for owner {UserId} was not delivered ({Reason}); a later run tries again.",
                channel.Kind, userId, result.Failure?.ToString() ?? result.Outcome.ToString());
            database.SentReminders.Remove(claim);
        }
        else
        {
            return;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
