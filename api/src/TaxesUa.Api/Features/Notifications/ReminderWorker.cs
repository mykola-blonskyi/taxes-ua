namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// Runs <see cref="ReminderSender"/> at startup and every <see cref="Interval"/> after. The plan is
/// recomputed from the ledger each time rather than scheduled, so a pass that finds nothing due costs
/// a few queries, and a server that was down catches up on its first pass back.
/// </summary>
internal sealed class ReminderWorker(
    ReminderSender sender,
    TimeProvider time,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await sender.RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "A reminder run failed.");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
