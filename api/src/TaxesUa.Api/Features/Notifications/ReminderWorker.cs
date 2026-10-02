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

    private long _runs;

    /// <summary>How many passes have finished. Tests use it to know that a pass ran, rather than sleeping
    /// and hoping it had time to.</summary>
    public long Runs => Volatile.Read(ref _runs);

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

            Interlocked.Increment(ref _runs);

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
