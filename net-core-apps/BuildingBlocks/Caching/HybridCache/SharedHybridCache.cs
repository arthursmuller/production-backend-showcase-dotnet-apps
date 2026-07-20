using System.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Inspacy.Caching;

public sealed class SharedHybridCache : ISharedCache, IDisposable
{
    private readonly Microsoft.Extensions.Caching.Hybrid.HybridCache _cache;
    private readonly InspacyHybridCacheOptions _options;
    private readonly SemaphoreSlim _sourceGate;
    private readonly TimeSpan _sourceQueueTimeout;

    public SharedHybridCache(
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        IOptions<InspacyHybridCacheOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceGate = new SemaphoreSlim(
            _options.SourceConcurrencyLimit,
            _options.SourceConcurrencyLimit);
        _sourceQueueTimeout = TimeSpan.FromMilliseconds(_options.SourceQueueTimeoutMilliseconds);
    }

    public async ValueTask<T> GetOrCreateAsync<T>(
        CacheKey key,
        Func<CancellationToken, ValueTask<T>> authoritativeSource,
        CacheEntryPolicy? policy = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(authoritativeSource);
        if (string.IsNullOrWhiteSpace(key.Value))
            throw new ArgumentException("A cache key created by ICacheKeyFactory is required.", nameof(key));

        CacheTelemetry.RecordRequest("get_or_create");
        using var activity = CacheTelemetry.ActivitySource.StartActivity("cache.get_or_create");
        var started = Stopwatch.GetTimestamp();

        if (!_options.Enabled)
        {
            try
            {
                return await LoadAuthoritativeAsync(authoritativeSource, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CacheTelemetry.RecordDuration("source", "get_or_create", started);
            }
        }

        var sourceInvoked = 0;
        var entryOptions = CreateEntryOptions(policy);

        try
        {
            var value = await _cache.GetOrCreateAsync(
                    key.Value,
                    async factoryToken =>
                    {
                        Interlocked.Exchange(ref sourceInvoked, 1);
                        CacheTelemetry.RecordMiss();
                        try
                        {
                            var loaded = await LoadAuthoritativeAsync(
                                    authoritativeSource,
                                    factoryToken)
                                .ConfigureAwait(false);
                            CacheTelemetry.RecordFill();
                            return loaded;
                        }
                        catch
                        {
                            CacheTelemetry.RecordError("source", "fill");
                            throw;
                        }
                    },
                    entryOptions,
                    tags: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (Volatile.Read(ref sourceInvoked) == 0)
                CacheTelemetry.RecordHit();

            return value;
        }
        finally
        {
            CacheTelemetry.RecordDuration("hybrid", "get_or_create", started);
        }
    }

    public async ValueTask RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key.Value))
            throw new ArgumentException("A cache key created by ICacheKeyFactory is required.", nameof(key));

        if (!_options.Enabled)
            return;

        CacheTelemetry.RecordRequest("remove");
        using var activity = CacheTelemetry.ActivitySource.StartActivity("cache.remove");
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _cache.RemoveAsync(key.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            CacheTelemetry.RecordError("hybrid", "remove");
            throw;
        }
        finally
        {
            CacheTelemetry.RecordDuration("hybrid", "remove", started);
        }
    }

    public void Dispose() => _sourceGate.Dispose();

    private HybridCacheEntryOptions CreateEntryOptions(CacheEntryPolicy? policy)
    {
        var expiration = policy?.Expiration
            ?? TimeSpan.FromSeconds(_options.DefaultExpirationSeconds);
        var localExpiration = policy?.LocalExpiration
            ?? TimeSpan.FromSeconds(_options.DefaultLocalExpirationSeconds);

        if (expiration <= TimeSpan.Zero || expiration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(policy), "L2 expiration must be between 1 ms and 1 hour.");

        if (localExpiration <= TimeSpan.Zero
            || localExpiration > TimeSpan.FromMinutes(5)
            || localExpiration > expiration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Local expiration must be positive, at most 5 minutes, and no longer than L2 expiration.");
        }

        return new HybridCacheEntryOptions
        {
            Expiration = expiration,
            LocalCacheExpiration = localExpiration,
        };
    }

    private async ValueTask<T> LoadAuthoritativeAsync<T>(
        Func<CancellationToken, ValueTask<T>> authoritativeSource,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var entered = await _sourceGate.WaitAsync(_sourceQueueTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!entered)
        {
            CacheTelemetry.RecordSourceRejection();
            throw new CacheSourceOverloadedException(_sourceQueueTimeout);
        }

        try
        {
            return await authoritativeSource(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sourceGate.Release();
        }
    }
}
