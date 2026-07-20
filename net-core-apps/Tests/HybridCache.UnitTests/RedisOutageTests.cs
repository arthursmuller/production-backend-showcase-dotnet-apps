using Inspacy.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HybridCache.UnitTests;

public sealed class RedisOutageTests
{
    [Fact]
    public async Task Hybrid_cache_returns_authoritative_value_during_secondary_outage()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        var options = EnabledOptions();
        var availability = new CacheAvailabilityState(TimeProvider.System);
        var resilient = new ResilientDistributedCache(
            new FailingDistributedCache(),
            options,
            availability,
            NullLogger<ResilientDistributedCache>.Instance);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<IDistributedCache>(resilient);
        services.AddHybridCache(hybrid =>
        {
            hybrid.MaximumKeyLength = options.Value.MaximumKeyLength;
            hybrid.MaximumPayloadBytes = options.Value.MaximumPayloadBytes;
        });
        services.AddSingleton<ISharedCache, SharedHybridCache>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var cache = provider.GetRequiredService<ISharedCache>();
        var key = new CacheKeyFactory(
                new CacheNamespace("test", "outage"),
                options)
            .Create("summary", "entity-7", 4);
        var sourceCalls = 0;

        var value = await cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref sourceCalls);
                return ValueTask.FromResult("from-database");
            },
            cancellationToken: testCancellation);

        Assert.Equal("from-database", value);
        Assert.Equal(1, sourceCalls);
        Assert.True(availability.GetSnapshot().Degraded);
    }

    [Fact]
    public async Task Redis_failures_are_fail_open_and_report_degraded_without_secrets()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        var options = EnabledOptions();
        var availability = new CacheAvailabilityState(TimeProvider.System);
        var cache = new ResilientDistributedCache(
            new FailingDistributedCache(),
            options,
            availability,
            NullLogger<ResilientDistributedCache>.Instance);
        var health = new OptionalRedisHealthCheck(cache, availability, options);

        Assert.Null(await cache.GetAsync("safe-key", testCancellation));
        await cache.SetAsync(
            "safe-key",
            new byte[] { 1, 2, 3 },
            new DistributedCacheEntryOptions(),
            testCancellation);

        var result = await health.CheckHealthAsync(new HealthCheckContext(), testCancellation);
        var snapshot = availability.GetSnapshot();

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.True(snapshot.Degraded);
        Assert.True(snapshot.FailureCount >= 3);
        Assert.Equal(nameof(InvalidOperationException), snapshot.LastFailureType);
        Assert.DoesNotContain("password", result.Description!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_probe_clears_degraded_state_after_redis_recovers()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        var options = EnabledOptions();
        var availability = new CacheAvailabilityState(TimeProvider.System);
        var secondary = new ToggleDistributedCache { Failing = true };
        var cache = new ResilientDistributedCache(
            secondary,
            options,
            availability,
            NullLogger<ResilientDistributedCache>.Instance);

        Assert.False(await cache.ProbeAsync(testCancellation));
        Assert.True(availability.GetSnapshot().Degraded);

        secondary.Failing = false;
        Assert.True(await cache.ProbeAsync(testCancellation));
        Assert.False(availability.GetSnapshot().Degraded);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_and_is_not_recorded_as_redis_failure()
    {
        var options = EnabledOptions(operationTimeoutMilliseconds: 5000);
        var availability = new CacheAvailabilityState(TimeProvider.System);
        var cache = new ResilientDistributedCache(
            new BlockingDistributedCache(),
            options,
            availability,
            NullLogger<ResilientDistributedCache>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetAsync("key", cancellation.Token));

        Assert.False(availability.GetSnapshot().Degraded);
        Assert.Equal(0, availability.GetSnapshot().FailureCount);
    }

    [Fact]
    public async Task Redis_operation_timeout_falls_through_and_marks_cache_degraded()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        var options = EnabledOptions(operationTimeoutMilliseconds: 25);
        var availability = new CacheAvailabilityState(TimeProvider.System);
        var cache = new ResilientDistributedCache(
            new BlockingDistributedCache(),
            options,
            availability,
            NullLogger<ResilientDistributedCache>.Instance);

        Assert.Null(await cache.GetAsync("key", testCancellation));
        Assert.True(availability.GetSnapshot().Degraded);
    }

    private static IOptions<InspacyHybridCacheOptions> EnabledOptions(
        int operationTimeoutMilliseconds = 100) => Options.Create(new InspacyHybridCacheOptions
        {
            Redis = new InspacyRedisCacheOptions
            {
                Enabled = true,
                ConnectionString = "redis.internal:6379,password=not-logged",
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            },
        });

    private sealed class FailingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw Failure();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromException<byte[]?>(Failure());
        public void Refresh(string key) => throw Failure();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
        public void Remove(string key) => throw Failure();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Failure();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Task.FromException(Failure());

        private static InvalidOperationException Failure() => new("password=must-never-be-observable");
    }

    private sealed class ToggleDistributedCache : IDistributedCache
    {
        public bool Failing { get; set; }

        public byte[]? Get(string key) => Failing ? throw new InvalidOperationException() : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Failing ? Task.FromException<byte[]?>(new InvalidOperationException()) : Task.FromResult<byte[]?>(null);
        public void Refresh(string key)
        {
            if (Failing)
                throw new InvalidOperationException();
        }
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Failing ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask;
        public void Remove(string key)
        {
            if (Failing)
                throw new InvalidOperationException();
        }
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Failing ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (Failing)
                throw new InvalidOperationException();
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Failing ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask;
    }

    private sealed class BlockingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
}
