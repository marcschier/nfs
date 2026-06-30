using System.Collections.Concurrent;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcDuplicateRequestCacheTests
{
    [Fact]
    public void GetOrStart_SameKey_InvokesFactoryOnceAndReturnsSameTask()
    {
        var cache = new RpcDuplicateRequestCache();
        var key = new RpcDuplicateRequestCache.Key("host", 42, 100003, 3, 1);
        int calls = 0;

        Task<byte[]> first = cache.GetOrStart(key, () => Run(ref calls));
        Task<byte[]> second = cache.GetOrStart(key, () => Run(ref calls));

        Assert.Same(first, second); // The duplicate replays the in-flight/completed original.
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrStart_DistinctKeys_InvokeFactoryPerKey()
    {
        var cache = new RpcDuplicateRequestCache();
        int calls = 0;

        Task<byte[]> a = cache.GetOrStart(new RpcDuplicateRequestCache.Key("a", 1, 0, 0, 0), () => Run(ref calls));
        Task<byte[]> b = cache.GetOrStart(new RpcDuplicateRequestCache.Key("b", 1, 0, 0, 0), () => Run(ref calls));

        Assert.NotSame(a, b);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetOrStart_PastCapacity_EvictsOldestFirst()
    {
        var cache = new RpcDuplicateRequestCache(capacity: 2);
        var calls = new ConcurrentDictionary<string, int>();

        Start(cache, calls, "a");
        Start(cache, calls, "b");
        Start(cache, calls, "c"); // Count exceeds capacity; the oldest entry ("a") is evicted.

        Start(cache, calls, "c"); // Still cached -> no second factory call.
        Start(cache, calls, "a"); // Evicted -> the factory runs again.

        Assert.Equal(2, calls["a"]);
        Assert.Equal(1, calls["c"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveCapacity_Throws(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpcDuplicateRequestCache(capacity));

    private static Task<byte[]> Run(ref int calls)
    {
        calls++;
        return Task.FromResult(Array.Empty<byte>());
    }

    private static void Start(RpcDuplicateRequestCache cache, ConcurrentDictionary<string, int> calls, string source) =>
        cache.GetOrStart(
            new RpcDuplicateRequestCache.Key(source, 0, 0, 0, 0),
            () =>
            {
                calls.AddOrUpdate(source, 1, (_, count) => count + 1);
                return Task.FromResult(Array.Empty<byte>());
            });
}
