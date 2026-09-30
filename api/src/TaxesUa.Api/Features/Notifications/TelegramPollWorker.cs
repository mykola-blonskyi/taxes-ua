namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// Long-polls Telegram for the whole life of the process when a bot token is configured, and does
/// nothing at all when it is not. A failed round waits 5, 10, 20 ... up to 60 seconds before the next.
/// </summary>
internal sealed class TelegramPollWorker(
    TelegramBot bot,
    TelegramPoller poller,
    TimeProvider time,
    ILogger<TelegramPollWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bot.IsConfigured)
        {
            return;
        }

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = false;
            try
            {
                succeeded = await poller.PollOnceAsync(TelegramPoller.LongPoll, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "A Telegram polling round failed.");
            }

            failures = succeeded ? 0 : failures + 1;
            if (!succeeded && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Backoff(failures), time, stoppingToken);
            }
        }
    }

    internal static TimeSpan Backoff(int failures)
    {
        var seconds = FirstBackoff.TotalSeconds * Math.Pow(2, Math.Min(failures, 10) - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }
}
