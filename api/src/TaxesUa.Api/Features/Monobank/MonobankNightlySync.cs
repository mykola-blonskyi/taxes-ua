using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Every night at <see cref="RunsAt"/> in Kyiv, queues a sync of every followed account whose token
/// was not rejected and sets each webhook again. A sync re-reads at least the last 31 days, so an
/// operation no webhook announced, or announced while the bank had disabled the webhook after three
/// failed deliveries, is still imported by morning.
/// </summary>
internal sealed class MonobankNightlySync(
    MonobankSyncQueue queue,
    MonobankWebhooks webhooks,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<MonobankNightlySync> logger) : BackgroundService
{
    public static readonly TimeOnly RunsAt = new(3, 0);

    public static DateTimeOffset NextRun(DateTimeOffset now)
    {
        var today = now.KyivDate();
        var run = today.InKyiv(RunsAt);
        return run > now ? run : today.AddDays(1).InKyiv(RunsAt);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            var now = time.GetUtcNow();
            await Task.Delay(NextRun(now) - now, time, stoppingToken);
            try
            {
                await EnqueueAllAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The nightly monobank sync could not be queued.");
            }
        }
    }

    private async Task EnqueueAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var syncable = await MonobankSyncWorker.Syncable(database).ToListAsync(cancellationToken);
        foreach (var work in syncable)
        {
            queue.Enqueue(work);
        }

        if (webhooks.IsConfigured)
        {
            foreach (var ownerId in syncable.Select(work => work.OwnerId).Distinct())
            {
                webhooks.Reconcile(ownerId);
            }
        }
    }
}
