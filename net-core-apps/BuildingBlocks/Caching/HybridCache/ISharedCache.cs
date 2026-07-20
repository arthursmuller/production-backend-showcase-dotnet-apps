namespace Inspacy.Caching;

public interface ISharedCache
{
    ValueTask<T> GetOrCreateAsync<T>(
        CacheKey key,
        Func<CancellationToken, ValueTask<T>> authoritativeSource,
        CacheEntryPolicy? policy = null,
        CancellationToken cancellationToken = default)
        where T : notnull;

    ValueTask RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);
}

public sealed record CacheEntryPolicy(
    TimeSpan Expiration,
    TimeSpan LocalExpiration);

public sealed class CacheSourceOverloadedException : Exception
{
    public CacheSourceOverloadedException(TimeSpan queueTimeout)
        : base($"The authoritative cache source concurrency budget was exhausted for {queueTimeout.TotalMilliseconds:F0} ms.")
    {
        QueueTimeout = queueTimeout;
    }

    public TimeSpan QueueTimeout { get; }
}
