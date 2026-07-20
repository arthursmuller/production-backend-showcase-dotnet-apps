using Inspacy.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HybridCache.UnitTests;

public sealed class SharedHybridCacheTests
{
    [Fact]
    public async Task Concurrent_same_key_misses_invoke_authoritative_source_once()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<ISharedCache>();
        var key = provider.GetRequiredService<ICacheKeyFactory>()
            .Create("comment-card", "42", 7);
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCalls = 0;

        async ValueTask<string> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sourceCalls);
            sourceStarted.TrySetResult();
            await releaseSource.Task.WaitAsync(cancellationToken);
            return "authoritative";
        }

        var requests = Enumerable.Range(0, 64)
            .Select(_ => cache.GetOrCreateAsync(
                key,
                Load,
                cancellationToken: testCancellation).AsTask())
            .ToArray();

        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        releaseSource.TrySetResult();
        var results = await Task.WhenAll(requests);

        Assert.All(results, value => Assert.Equal("authoritative", value));
        Assert.Equal(1, Volatile.Read(ref sourceCalls));

        var cached = await cache.GetOrCreateAsync(
            key,
            _ => ValueTask.FromResult("wrong"),
            cancellationToken: testCancellation);
        Assert.Equal("authoritative", cached);
        Assert.Equal(1, Volatile.Read(ref sourceCalls));
    }

    [Fact]
    public async Task Distinct_misses_cannot_exceed_authoritative_source_concurrency_budget()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Cache:SourceConcurrencyLimit"] = "1",
            ["Cache:SourceQueueTimeoutMilliseconds"] = "25",
        });
        var cache = provider.GetRequiredService<ISharedCache>();
        var keys = provider.GetRequiredService<ICacheKeyFactory>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = cache.GetOrCreateAsync(
            keys.Create("relation", "one", 1),
            async cancellationToken =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return "one";
            }, cancellationToken: testCancellation).AsTask();

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        await Assert.ThrowsAsync<CacheSourceOverloadedException>(() =>
            cache.GetOrCreateAsync(
                keys.Create("relation", "two", 1),
                _ => ValueTask.FromResult("two"),
                cancellationToken: testCancellation).AsTask());

        releaseFirst.TrySetResult();
        Assert.Equal("one", await first);
    }

    [Fact]
    public async Task Disabling_cache_bypasses_storage_but_keeps_source_budget_and_cancellation()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Cache:Enabled"] = "false",
        });
        var cache = provider.GetRequiredService<ISharedCache>();
        var key = provider.GetRequiredService<ICacheKeyFactory>()
            .Create("summary", "99", 1);
        var calls = 0;

        _ = await cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(1);
            },
            cancellationToken: testCancellation);
        _ = await cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(1);
            },
            cancellationToken: testCancellation);

        Assert.Equal(2, calls);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetOrCreateAsync(
                key,
                async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return 0;
                },
                cancellationToken: cancelled.Token).AsTask());
    }

    private static ServiceProvider CreateProvider(
        Dictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInspacyHybridCache(configuration, "test", "cache-tests");
        return services.BuildServiceProvider(validateScopes: true);
    }
}
