using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// The one place the bank's statement is read. Endpoints only enqueue; this worker takes one account at
/// a time, so the rate gate's waits never hold a request thread. The queue lives in memory, so on start
/// it queues again every followed account whose statement is not read up to the last window.
/// </summary>
internal sealed class MonobankSyncWorker(
    MonobankSyncQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<MonobankSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnqueueUnfinishedAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An unhandled exception here would stop the whole host; the next "sync now" still works.
            logger.LogError(exception, "Unfinished monobank syncs could not be queued at startup.");
        }

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
                await RecordUnexpectedAsync(work, stoppingToken);
            }
            finally
            {
                await RefreshReserveJarAsync(work, stoppingToken);
                queue.Complete(work);
            }
        }
    }

    // After the statement, so a slow or failed jar read never delays the operations, and before the work is
    // complete, so a sync that reads as finished has refreshed the jar too. The read takes the gate's
    // client-info slot only when it is free; several accounts of one owner in a run share one call.
    private async Task RefreshReserveJarAsync(SyncWork work, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<ReserveJarService>()
                .RefreshAsync(work.OwnerId, stoppingToken);
            if (outcome is JarOutcome.Unavailable or JarOutcome.InvalidToken)
            {
                logger.LogWarning("The reserve jar of owner {OwnerId} was not refreshed: {Outcome}.", work.OwnerId, outcome.GetType().Name);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The reserve jar of owner {OwnerId} could not be refreshed.", work.OwnerId);
        }
    }

    private async Task EnqueueUnfinishedAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var behind = time.GetUtcNow() - MonobankStatementImport.Window;
        var unfinished = await Syncable(
                database, account => account.SyncedThrough == null || account.SyncedThrough < behind)
            .ToListAsync(stoppingToken);
        foreach (var work in unfinished)
        {
            queue.Enqueue(work);
        }
    }

    // Every followed FOP account whose owner's token was not rejected, narrowed by where.
    internal static IQueryable<SyncWork> Syncable(
        AppDbContext database, Expression<Func<BankAccount, bool>>? where = null) =>
        database.BankAccounts
            .Where(account => account.Bank == Bank.Monobank
                && account.IsFop
                && account.IsActive
                && database.MonobankConnections.Any(connection =>
                    connection.UserId == account.UserId && connection.RejectedAt == null))
            .Where(where ?? (_ => true))
            .Select(account => new SyncWork(account.UserId, account.Id));

    private async Task RecordUnexpectedAsync(SyncWork work, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MonobankStatementImport>()
                .RecordFailureAsync(work.BankAccountId, SyncFailure.Unexpected, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The failed sync of account {BankAccountId} could not be recorded.", work.BankAccountId);
        }
    }
}
