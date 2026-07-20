namespace Inspacy.Caching;

public sealed record CacheAvailabilitySnapshot(
    bool Degraded,
    long FailureCount,
    DateTimeOffset? LastFailureUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastFailureType);

public sealed class CacheAvailabilityState(TimeProvider timeProvider)
{
    private long _failureCount;
    private long _lastFailureUtcTicks;
    private long _lastSuccessUtcTicks;
    private string? _lastFailureType;
    private int _degraded;

    public bool RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Interlocked.Increment(ref _failureCount);
        Interlocked.Exchange(ref _lastFailureUtcTicks, timeProvider.GetUtcNow().UtcTicks);
        Volatile.Write(ref _lastFailureType, exception.GetType().Name);
        return Interlocked.Exchange(ref _degraded, 1) == 0;
    }

    public bool RecordSuccess()
    {
        Interlocked.Exchange(ref _lastSuccessUtcTicks, timeProvider.GetUtcNow().UtcTicks);
        return Interlocked.Exchange(ref _degraded, 0) == 1;
    }

    public CacheAvailabilitySnapshot GetSnapshot()
    {
        var failureTicks = Interlocked.Read(ref _lastFailureUtcTicks);
        var successTicks = Interlocked.Read(ref _lastSuccessUtcTicks);
        return new CacheAvailabilitySnapshot(
            Volatile.Read(ref _degraded) == 1,
            Interlocked.Read(ref _failureCount),
            failureTicks == 0 ? null : new DateTimeOffset(failureTicks, TimeSpan.Zero),
            successTicks == 0 ? null : new DateTimeOffset(successTicks, TimeSpan.Zero),
            Volatile.Read(ref _lastFailureType));
    }
}
