using System.Collections.Concurrent;

namespace TaxesUa.Api.Features.Monobank;

internal abstract record JarsRead
{
    private JarsRead() { }

    // At is when the bank answered, which can be earlier than now: an answer is reused for one gate interval.
    public sealed record Found(IReadOnlyList<MonobankJar> Jars, DateTimeOffset At) : JarsRead;

    // The gate's slot is taken by another call, so asking the bank now would break monobank's limit.
    public sealed record Waiting(TimeSpan RetryAfter) : JarsRead;

    public sealed record InvalidToken : JarsRead;

    public sealed record Unavailable(string Reason) : JarsRead;
}

/// <summary>
/// The one way the jars are read: <c>client-info</c> through <see cref="MonobankClient"/> and the same
/// <see cref="MonobankRateGate"/> slot every other <c>client-info</c> call uses. The gate would make a
/// second call within a minute wait, which a request or the sync worker must not do, so a read takes the
/// slot only when it is free, and the last answer is reused for the interval it covers. Listing the jars,
/// choosing one and refreshing its balance therefore cost the bank one call between them.
/// The answer is held in memory per owner and never leaves the process; a token is never part of it.
/// </summary>
internal sealed class MonobankJarReader(MonobankRateGate gate, TimeProvider time)
{
    public const string Method = "client-info";

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<MonobankJar> Jars)> _last = new();

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // Bumped whenever the owner's token changes. A read that began under an earlier number belongs to a
    // token that is gone, so it must not fill the cache or be believed.
    private readonly ConcurrentDictionary<string, int> _generation = new();

    // The client is the caller's, resolved from its scope, so this singleton does not pin one handler.
    public async Task<JarsRead> ReadAsync(
        MonobankClient client, string ownerId, string token, CancellationToken cancellationToken)
    {
        var owners = _locks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await owners.WaitAsync(cancellationToken);
        try
        {
            if (_last.TryGetValue(ownerId, out var last) && time.GetUtcNow() - last.At < MonobankRateGate.Interval)
            {
                return new JarsRead.Found(last.Jars, last.At);
            }

            if (!gate.TryTakeTurn(ownerId, Method, out var retryAfter))
            {
                return new JarsRead.Waiting(retryAfter);
            }

            var generation = Generation(ownerId);
            var result = await client.GetClientInfoAsync(token, cancellationToken);
            if (Generation(ownerId) != generation)
            {
                return new JarsRead.Unavailable("The monobank token changed while it was being read.");
            }

            switch (result)
            {
                case ClientInfoResult.Found found:
                    var at = time.GetUtcNow();
                    _last[ownerId] = (at, found.Info.Jars);
                    return new JarsRead.Found(found.Info.Jars, at);
                case ClientInfoResult.InvalidToken:
                    _last.TryRemove(ownerId, out _);
                    return new JarsRead.InvalidToken();
                case ClientInfoResult.Unavailable unavailable:
                    return new JarsRead.Unavailable(unavailable.Reason);
                default:
                    throw new InvalidOperationException($"Unhandled {nameof(ClientInfoResult)}.");
            }
        }
        finally
        {
            owners.Release();
        }
    }

    /// <summary>
    /// Keeps the answer of a <c>client-info</c> call made when the token was saved, which spent the
    /// slot, so that listing the jars right after needs no second call.
    /// </summary>
    public void Remember(string ownerId, MonobankClientInfo info)
    {
        gate.Mark(ownerId, Method);
        _generation.AddOrUpdate(ownerId, 1, (_, number) => number + 1);
        _last[ownerId] = (time.GetUtcNow(), info.Jars);
    }

    /// <summary>Drops what was read with a token that is gone.</summary>
    public void Forget(string ownerId)
    {
        _generation.AddOrUpdate(ownerId, 1, (_, number) => number + 1);
        _last.TryRemove(ownerId, out _);
    }

    private int Generation(string ownerId) => _generation.GetValueOrDefault(ownerId);
}
