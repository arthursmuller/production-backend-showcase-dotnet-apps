using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inspacy.Caching;

public sealed class ResilientDistributedCache : IDistributedCache
{
    private const string HealthProbeKey = "__inspacy_cache_health_probe__";
    private readonly IDistributedCache _secondaryCache;
    private readonly InspacyRedisCacheOptions _options;
    private readonly CacheAvailabilityState _availability;
    private readonly ILogger<ResilientDistributedCache> _logger;

    public ResilientDistributedCache(
        IDistributedCache secondaryCache,
        IOptions<InspacyHybridCacheOptions> options,
        CacheAvailabilityState availability,
        ILogger<ResilientDistributedCache> logger)
    {
        _secondaryCache = secondaryCache ?? throw new ArgumentNullException(nameof(secondaryCache));
        _options = options?.Value.Redis ?? throw new ArgumentNullException(nameof(options));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[]? Get(string key) => ExecuteSync(
        "get",
        () => _secondaryCache.Get(key),
        fallback: null);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => ExecuteAsync(
        "get",
        innerToken => _secondaryCache.GetAsync(key, innerToken),
        fallback: null,
        token);

    public void Refresh(string key) => ExecuteSync(
        "refresh",
        () =>
        {
            _secondaryCache.Refresh(key);
            return true;
        },
        fallback: false);

    public Task RefreshAsync(string key, CancellationToken token = default) => ExecuteVoidAsync(
        "refresh",
        innerToken => _secondaryCache.RefreshAsync(key, innerToken),
        token);

    public void Remove(string key) => ExecuteSync(
        "remove",
        () =>
        {
            _secondaryCache.Remove(key);
            return true;
        },
        fallback: false);

    public Task RemoveAsync(string key, CancellationToken token = default) => ExecuteVoidAsync(
        "remove",
        innerToken => _secondaryCache.RemoveAsync(key, innerToken),
        token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        CacheTelemetry.RecordPayloadSize(value.LongLength);
        ExecuteSync(
            "set",
            () =>
            {
                _secondaryCache.Set(key, value, options);
                return true;
            },
            fallback: false);
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        CacheTelemetry.RecordPayloadSize(value.LongLength);
        return ExecuteVoidAsync(
            "set",
            innerToken => _secondaryCache.SetAsync(key, value, options, innerToken),
            token);
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return true;

        var started = Stopwatch.GetTimestamp();
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            _ = await _secondaryCache.GetAsync(HealthProbeKey, timeout.Token).ConfigureAwait(false);
            RecordSuccess();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RecordFailure(exception);
            CacheTelemetry.RecordError("l2", "probe");
            return false;
        }
        finally
        {
            CacheTelemetry.RecordDuration("l2", "probe", started);
        }
    }

    private T? ExecuteSync<T>(string operation, Func<T?> action, T? fallback)
    {
        if (!_options.Enabled)
            return fallback;

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = action();
            RecordSuccess();
            return result;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RecordFailure(exception);
            CacheTelemetry.RecordError("l2", operation);
            return fallback;
        }
        finally
        {
            CacheTelemetry.RecordDuration("l2", operation, started);
        }
    }

    private async Task<T?> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<T?>> action,
        T? fallback,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return fallback;

        var started = Stopwatch.GetTimestamp();
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            var result = await action(timeout.Token).ConfigureAwait(false);
            RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RecordFailure(exception);
            CacheTelemetry.RecordError("l2", operation);
            return fallback;
        }
        finally
        {
            CacheTelemetry.RecordDuration("l2", operation, started);
        }
    }

    private async Task ExecuteVoidAsync(
        string operation,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var started = Stopwatch.GetTimestamp();
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            await action(timeout.Token).ConfigureAwait(false);
            RecordSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RecordFailure(exception);
            CacheTelemetry.RecordError("l2", operation);
        }
        finally
        {
            CacheTelemetry.RecordDuration("l2", operation, started);
        }
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.OperationTimeoutMilliseconds));
        return timeout;
    }

    private void RecordFailure(Exception exception)
    {
        if (_availability.RecordFailure(exception))
        {
            _logger.LogWarning(
                "Redis L2 cache became unavailable ({ExceptionType}); requests will use L1 or the bounded authoritative source.",
                exception.GetType().Name);
        }
    }

    private void RecordSuccess()
    {
        if (_availability.RecordSuccess())
            _logger.LogInformation("Redis L2 cache recovered.");
    }

    private static bool IsRecoverable(Exception exception) => exception is not (
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException);
}
