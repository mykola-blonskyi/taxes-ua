using System.Collections.Concurrent;

namespace TaxesUa.Api.Features.Monobank;

internal abstract record ClientInfoRead
{
    private ClientInfoRead() { }

    // At is when the bank answered, which can be earlier than now: an answer is reused for one gate interval.
    public sealed record Found(string Name, IReadOnlyList<MonobankJar> Jars, DateTimeOffset At) : ClientInfoRead;

    // The gate's slot is taken by another call, so asking the bank now would break monobank's limit.
    public sealed record Waiting(TimeSpan RetryAfter) : ClientInfoRead;

    public sealed record InvalidToken : ClientInfoRead;

    public sealed record Unavailable(string Reason) : ClientInfoRead;
}

/// <summary>
/// The one way <c>client-info</c> (the owner's name and jars) is read: through <see cref="MonobankClient"/>
/// and the same <see cref="MonobankRateGate"/> slot every other <c>client-info</c> call uses. The gate would make a
/// second call within a minute wait, which a request or the sync worker must not do, so a read takes the
/// slot only when it is free, and the last answer is reused for the interval it covers. Listing the jars,
/// choosing one, refreshing its balance and the invoicing prefill therefore cost the bank one call between them.
/// Only the name and the jars are held, in memory per owner; the accounts and their IBANs are not, and a token
/// is never part of it.
/// </summary>
internal sealed class MonobankClientInfoReader(MonobankRateGate gate, TimeProvider time)
{
    public const string Method = "client-info";

    // Guards the two maps below. Held for a few instructions only, never across a bank call, so a token
    // save or a disconnect is never parked behind a read in flight.
    private readonly object _state = new();

    private readonly Dictionary<string, (DateTimeOffset At, string Name, IReadOnlyList<MonobankJar> Jars)> _last = [];

    // Bumped whenever the owner's token changes. A read that began under an earlier number belongs to a
    // token that is gone, so it must not fill the cache or be believed.
    private readonly Dictionary<string, int> _generation = [];

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// The number to take before loading the owner's token and to hand to <see cref="ReadAsync"/>, so a
    /// token saved or dropped while this one is loaded, decrypted or waiting for its turn is noticed.
    /// </summary>
    public int Generation(string ownerId)
    {
        lock (_state)
        {
            return _generation.GetValueOrDefault(ownerId);
        }
    }

    // The client is the caller's, resolved from its scope, so this singleton does not pin one handler.
    public async Task<ClientInfoRead> ReadAsync(
        MonobankClient client, string ownerId, string token, int generation, CancellationToken cancellationToken)
    {
        var owners = _locks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await owners.WaitAsync(cancellationToken);
        try
        {
            lock (_state)
            {
                if (_generation.GetValueOrDefault(ownerId) != generation)
                {
                    return TokenChanged();
                }

                if (_last.TryGetValue(ownerId, out var last) && time.GetUtcNow() - last.At < MonobankRateGate.Interval)
                {
                    return new ClientInfoRead.Found(last.Name, last.Jars, last.At);
                }
            }

            if (!gate.TryTakeTurn(ownerId, Method, out var retryAfter))
            {
                return new ClientInfoRead.Waiting(retryAfter);
            }

            var result = await client.GetClientInfoAsync(token, cancellationToken);

            // The check and the write are one step, so a token saved a moment ago cannot be overwritten
            // by the answer of the token it replaced.
            lock (_state)
            {
                if (_generation.GetValueOrDefault(ownerId) != generation)
                {
                    return TokenChanged();
                }

                switch (result)
                {
                    case ClientInfoResult.Found found:
                        var at = time.GetUtcNow();
                        _last[ownerId] = (at, found.Info.Name, found.Info.Jars);
                        return new ClientInfoRead.Found(found.Info.Name, found.Info.Jars, at);
                    case ClientInfoResult.InvalidToken:
                        _last.Remove(ownerId);
                        return new ClientInfoRead.InvalidToken();
                    case ClientInfoResult.Unavailable unavailable:
                        return new ClientInfoRead.Unavailable(unavailable.Reason);
                    default:
                        throw new InvalidOperationException($"Unhandled {nameof(ClientInfoResult)}.");
                }
            }
        }
        finally
        {
            owners.Release();
        }
    }

    /// <summary>
    /// Keeps the answer of a <c>client-info</c> call made when the token was saved, which spent the
    /// slot, so that listing the jars or the invoicing prefill right after needs no second call.
    /// </summary>
    public void Remember(string ownerId, MonobankClientInfo info)
    {
        gate.Mark(ownerId, Method);
        lock (_state)
        {
            _generation[ownerId] = _generation.GetValueOrDefault(ownerId) + 1;
            _last[ownerId] = (time.GetUtcNow(), info.Name, info.Jars);
        }
    }

    /// <summary>Drops what was read with a token that is gone.</summary>
    public void Forget(string ownerId)
    {
        lock (_state)
        {
            _generation[ownerId] = _generation.GetValueOrDefault(ownerId) + 1;
            _last.Remove(ownerId);
        }
    }

    private static ClientInfoRead.Unavailable TokenChanged() =>
        new("The monobank token changed while it was being read.");
}
