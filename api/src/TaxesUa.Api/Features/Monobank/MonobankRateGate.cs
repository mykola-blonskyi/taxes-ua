namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// monobank allows one call per method per 60 seconds for a token. One owner holds one token, so the
/// gate is keyed by owner and method. A caller reserves the next free slot and waits for it on
/// <see cref="TimeProvider"/>, so tests advance a fake clock instead of sleeping.
/// </summary>
internal sealed class MonobankRateGate(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly Dictionary<(string OwnerId, string Method), DateTimeOffset> _nextSlot = [];

    private readonly Lock _lock = new();

    /// <summary>
    /// Takes the slot only when it is free now, for a caller that must not park a request or the sync
    /// worker for up to a minute. False, with how long until the slot frees, when it is not.
    /// </summary>
    public bool TryTakeTurn(string ownerId, string method, out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_nextSlot.TryGetValue((ownerId, method), out var next) && next > now)
            {
                retryAfter = next - now;
                return false;
            }

            _nextSlot[(ownerId, method)] = now + Interval;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>Records a call made outside the gate, so the next turn starts a full interval after it.</summary>
    public void Mark(string ownerId, string method)
    {
        lock (_lock)
        {
            var earliest = time.GetUtcNow() + Interval;
            if (!_nextSlot.TryGetValue((ownerId, method), out var next) || next < earliest)
            {
                _nextSlot[(ownerId, method)] = earliest;
            }
        }
    }

    public async Task WaitTurnAsync(string ownerId, string method, CancellationToken cancellationToken)
    {
        DateTimeOffset slot;
        lock (_lock)
        {
            var now = time.GetUtcNow();
            slot = _nextSlot.TryGetValue((ownerId, method), out var next) && next > now ? next : now;
            _nextSlot[(ownerId, method)] = slot + Interval;
        }

        var wait = slot - time.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, cancellationToken);
        }

        // A wait that overran its slot (a paused VM, a long GC) would leave the next reservation in the
        // past and let two calls out back to back, so the next slot counts from the call itself.
        lock (_lock)
        {
            var earliest = time.GetUtcNow() + Interval;
            if (_nextSlot[(ownerId, method)] < earliest)
            {
                _nextSlot[(ownerId, method)] = earliest;
            }
        }
    }
}
