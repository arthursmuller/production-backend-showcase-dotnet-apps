using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence;

public sealed class FeedUserErasureHealthCheck(
    FeedContext context,
    IOptions<FeedUserErasureOptions> configuredOptions,
    TimeProvider timeProvider) : IHealthCheck
{
    private readonly FeedUserErasureOptions _options = configuredOptions.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var oldest = await context.FeedUserErasures.AsNoTracking()
            .Where(item => item.Status != FeedUserErasureStatus.Completed)
            .MinAsync(item => (DateTime?)item.RequestedAtUtc, cancellationToken);
        if (oldest.HasValue
            && now - FeedUserErasureScheduler.NormalizeUtc(oldest.Value)
                > TimeSpan.FromMinutes(_options.MaximumPendingAgeMinutes))
        {
            return HealthCheckResult.Degraded(
                "Feed privacy erasure backlog exceeded its bounded age objective.",
                data: new Dictionary<string, object>
                {
                    ["oldestPendingAgeSeconds"] = Math.Max(
                        0,
                        (now - FeedUserErasureScheduler.NormalizeUtc(oldest.Value)).TotalSeconds),
                });
        }
        return HealthCheckResult.Healthy("Feed privacy erasure backlog is within budget.");
    }
}
