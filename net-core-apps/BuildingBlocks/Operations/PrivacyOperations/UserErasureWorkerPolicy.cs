namespace Inspacy.Privacy;

/// <summary>
/// The common operational envelope for a bounded, leased user-erasure worker.
/// Service-owned options map to this value without changing their configuration schema.
/// </summary>
public readonly record struct UserErasureWorkerSettings(
    int BatchSize,
    int MaximumBatchesPerCycle,
    int CycleTimeBudgetMilliseconds,
    int LeaseSeconds,
    int ActiveDelayMilliseconds,
    int IdleDelayMilliseconds,
    int FailureDelayMilliseconds,
    int MaximumPendingAgeMinutes);

/// <summary>
/// The two bounds that legitimately vary with a service's storage workload.
/// All other limits are platform policy.
/// </summary>
public readonly record struct UserErasureWorkerLimits(
    int MaximumBatchSize,
    int MinimumLeaseSeconds);

public static class UserErasureWorkerPolicy
{
    public static bool IsValid(
        in UserErasureWorkerSettings settings,
        in UserErasureWorkerLimits limits) =>
        limits.MaximumBatchSize >= 1
        && limits.MinimumLeaseSeconds >= 1
        && settings.BatchSize is >= 1
        && settings.BatchSize <= limits.MaximumBatchSize
        && settings.MaximumBatchesPerCycle is >= 1 and <= 32
        && settings.CycleTimeBudgetMilliseconds is >= 100 and <= 10_000
        && settings.LeaseSeconds >= limits.MinimumLeaseSeconds
        && settings.LeaseSeconds <= 300
        && settings.LeaseSeconds * 1_000L
            >= settings.CycleTimeBudgetMilliseconds * 2L
        && settings.ActiveDelayMilliseconds is >= 10 and <= 10_000
        && settings.IdleDelayMilliseconds is >= 100 and <= 60_000
        && settings.FailureDelayMilliseconds is >= 100 and <= 60_000
        && settings.MaximumPendingAgeMinutes is >= 1 and <= 1_440;
}
