using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TaxesUa.Api.Features.Monobank;

internal sealed record SyncWork(string OwnerId, Guid BankAccountId);

/// <summary>
/// Work waiting for <see cref="MonobankSyncWorker"/>. An account already waiting is not queued twice;
/// one that is running can be queued again, so a sync asked for mid-run still reads the newest operations.
/// </summary>
internal sealed class MonobankSyncQueue
{
    private readonly Channel<SyncWork> _channel =
        Channel.CreateUnbounded<SyncWork>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<SyncWork, byte> _waiting = new();

    private readonly ConcurrentDictionary<SyncWork, byte> _running = new();

    public void Enqueue(SyncWork work)
    {
        if (_waiting.TryAdd(work, 0))
        {
            _channel.Writer.TryWrite(work);
        }
    }

    /// <summary>True when nothing is waiting or running. Tests use it to know that no sync was started,
    /// rather than sleeping and hoping it had time to.</summary>
    public bool IsIdle => _waiting.IsEmpty && _running.IsEmpty;

    public bool IsPending(SyncWork work) => _waiting.ContainsKey(work) || _running.ContainsKey(work);

    public async IAsyncEnumerable<SyncWork> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            _running.TryAdd(work, 0);
            _waiting.TryRemove(work, out _);
            yield return work;
        }
    }

    public void Complete(SyncWork work) => _running.TryRemove(work, out _);
}
