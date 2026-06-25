using System.Collections.Concurrent;

namespace Nfs.Rpc;

/// <summary>
/// A bounded duplicate-request cache (DRC) for the connectionless (UDP) transport. It coalesces a
/// retransmitted request with the in-flight original — and replays a completed reply to a late
/// retransmit — so that non-idempotent procedures are not executed twice. Entries are evicted in
/// roughly first-in, first-out order once the capacity is exceeded.
/// </summary>
internal sealed class RpcDuplicateRequestCache
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<Key, Lazy<Task<byte[]>>> _entries = new();
    private readonly ConcurrentQueue<Key> _order = new();

    /// <summary>Creates a cache that retains at most <paramref name="capacity"/> recent replies.</summary>
    /// <param name="capacity">The maximum number of cached entries.</param>
    public RpcDuplicateRequestCache(int capacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>
    /// Returns the reply task for the given request, starting it exactly once. A duplicate request
    /// receives the same in-flight or completed task as the original.
    /// </summary>
    /// <param name="key">The request identity.</param>
    /// <param name="factory">Produces the reply when the request is seen for the first time.</param>
    /// <returns>The reply task.</returns>
    public Task<byte[]> GetOrStart(Key key, Func<Task<byte[]>> factory)
    {
        bool added = false;
        Lazy<Task<byte[]>> entry = _entries.GetOrAdd(
            key,
            _ =>
            {
                added = true;
                return new Lazy<Task<byte[]>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
            });

        if (added)
        {
            _order.Enqueue(key);
            Trim();
        }

        return entry.Value;
    }

    private void Trim()
    {
        while (_entries.Count > _capacity && _order.TryDequeue(out Key evicted))
        {
            _entries.TryRemove(evicted, out _);
        }
    }

    /// <summary>Identifies a request for duplicate detection.</summary>
    /// <param name="Source">The caller's transport address.</param>
    /// <param name="Xid">The transaction id.</param>
    /// <param name="Program">The program number.</param>
    /// <param name="Version">The program version.</param>
    /// <param name="Procedure">The procedure number.</param>
    public readonly record struct Key(string Source, uint Xid, uint Program, uint Version, uint Procedure);
}
