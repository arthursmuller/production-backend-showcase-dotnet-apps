using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Inspacy.Caching;

public sealed class OptionalRedisHealthCheck(
    ResilientDistributedCache distributedCache,
    CacheAvailabilityState availability,
    IOptions<InspacyHybridCacheOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Redis.Enabled)
        {
            return HealthCheckResult.Healthy(
                "Redis L2 is disabled; HybridCache retains L1 stampede protection and the database remains authoritative.");
        }

        var available = await distributedCache.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = availability.GetSnapshot();
        var data = new Dictionary<string, object>
        {
            ["optional"] = true,
            ["failure_count"] = snapshot.FailureCount,
        };

        if (snapshot.LastFailureUtc is { } lastFailure)
            data["last_failure_utc"] = lastFailure;
        if (snapshot.LastSuccessUtc is { } lastSuccess)
            data["last_success_utc"] = lastSuccess;
        if (snapshot.LastFailureType is { } failureType)
            data["last_failure_type"] = failureType;

        return available
            ? HealthCheckResult.Healthy("Redis L2 is available.", data)
            : HealthCheckResult.Degraded(
                "Redis L2 is unavailable; requests fail open to L1 or the bounded authoritative source.",
                data: data);
    }
}
