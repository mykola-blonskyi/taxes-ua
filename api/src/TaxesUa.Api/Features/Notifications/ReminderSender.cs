using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// One pass over every owner with a switched-on channel: what <see cref="ReminderPlan"/> says is due
/// at this Kyiv moment goes to each such channel once. Each send is claimed in
/// <see cref="SentReminder"/> first. A failure that may pass (Telegram down, a 429, the channel
/// switched off meanwhile) gives the claim back so a later pass tries again within the window; a
/// failure that will not pass (blocked, rejected) keeps it. A send that has begun is not cut off by the host
/// stopping, so a claim is never left undecided.
/// The same pass sends the open <see cref="Incident"/>s of every <see cref="IIncidentSource"/>, each
/// claimed once per channel under its own key.
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

            try
            {
                await RunIncidentsForOwnerAsync(userId, today, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Incident alerts for owner {UserId} could not be sent.", userId);
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

        var channels = await ChannelsOfAsync(scope.ServiceProvider, database, userId, cancellationToken);

        foreach (var reminder in reminders)
        {
            var message = ReminderTexts.Render(reminder, settings.Locale, today, link.Url);
            foreach (var channel in channels)
            {
                await SendAsync(database, channel, userId, reminder, message, cancellationToken);
            }
        }
    }

    private async Task RunIncidentsForOwnerAsync(string userId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var incidents = new List<Incident>();
        foreach (var source in scope.ServiceProvider.GetServices<IIncidentSource>())
        {
            incidents.AddRange(await source.OpenAsync(database, userId, cancellationToken));
        }

        if (incidents.Count == 0)
        {
            return;
        }

        var locale = (await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken)).Locale;
        var channels = await ChannelsOfAsync(scope.ServiceProvider, database, userId, cancellationToken);
        foreach (var incident in incidents)
        {
            var message = IncidentTexts.Render(incident, locale, link.Url);
            foreach (var channel in channels)
            {
                // The claim's unique index is what makes this at most once; the look first only spares a
                // failed insert on every pass for an incident that was sent long ago.
                if (await database.SentReminders.AnyAsync(
                        row => row.UserId == userId && row.Incident == incident.Key && row.Channel == channel.Kind,
                        cancellationToken))
                {
                    continue;
                }

                var claim = new SentReminder
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Date = today,
                    Kinds = ReminderKinds.None,
                    Offset = ReminderOffset.OnTheDay,
                    Channel = channel.Kind,
                    Incident = incident.Key,
                    ClaimedAt = time.GetUtcNow(),
                };
                await ClaimAndSendAsync(database, channel, claim, message, cancellationToken);
            }
        }
    }

    private static async Task<IReminderChannel[]> ChannelsOfAsync(
        IServiceProvider services, AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var enabled = await database.NotificationChannels
            .Where(row => row.UserId == userId && row.Enabled && row.ConfirmedAt != null)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        return
        [
            .. services.GetServices<IReminderChannel>()
                .Where(channel => channel.IsAvailable && enabled.Contains(channel.Kind)),
        ];
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
                    && row.Incident == string.Empty
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
        await ClaimAndSendAsync(database, channel, claim, message, cancellationToken);
    }

    private async Task ClaimAndSendAsync(
        AppDbContext database,
        IReminderChannel channel,
        SentReminder claim,
        ReminderMessage message,
        CancellationToken cancellationToken)
    {
        // A stop requested before the claim is a plain stop: nothing is claimed, so the next start sends it.
        cancellationToken.ThrowIfCancellationRequested();
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

        // From here the claim is outstanding, so the send and the save that settles it run to their
        // end even when the host is stopping: the channel's own timeout bounds the send, and a send cut
        // off by the host would leave a claim that is neither delivered nor given back, so the reminder
        // would never be sent again.
        var result = await channel.SendAsync(claim.UserId, message, CancellationToken.None);
        if (result.Outcome == DeliveryOutcome.Sent)
        {
            claim.DeliveredAt = time.GetUtcNow();
        }
        else if (result.Failure is not { } failure || failure.IsTransient())
        {
            logger.LogInformation(
                "A {Channel} reminder for owner {UserId} was not delivered ({Reason}); a later run tries again.",
                channel.Kind, claim.UserId, result.Failure?.ToString() ?? result.Outcome.ToString());
            database.SentReminders.Remove(claim);
        }
        else
        {
            return;
        }

        await database.SaveChangesAsync(CancellationToken.None);
    }
}
