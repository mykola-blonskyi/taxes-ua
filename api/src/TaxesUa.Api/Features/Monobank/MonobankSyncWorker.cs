namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// The one place the bank's statement is read. Endpoints only enqueue; this worker takes one account at
/// a time, so the rate gate's waits never hold a request thread.
/// </summary>
internal sealed class MonobankSyncWorker(
    MonobankSyncQueue queue,
    IServiceScopeFactory scopes,
    ILogger<MonobankSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MonobankStatementImport>().RunAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "monobank sync of account {BankAccountId} failed.", work.BankAccountId);
            }
            finally
            {
                queue.Complete(work);
            }
        }
    }
}
