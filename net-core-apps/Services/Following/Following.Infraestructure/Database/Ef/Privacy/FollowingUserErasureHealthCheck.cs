using Following.Application.Providers.Privacy;
using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class FollowingUserErasureHealthCheck(
        FollowingContext context,
        IOptions<FollowingUserErasureOptions> configuredOptions,
        TimeProvider timeProvider) : IHealthCheck
    {
        private readonly FollowingUserErasureOptions _options = configuredOptions.Value;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext healthContext,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var oldest = await context.UserErasures
                .AsNoTracking()
                .Where(item => item.Status != FollowingUserErasureStatus.Completed)
                .MinAsync(item => (DateTime?)item.RequestedAtUtc, cancellationToken);
            if (oldest.HasValue
                && nowUtc - NormalizeUtc(oldest.Value)
                    > TimeSpan.FromMinutes(_options.MaxPendingAgeMinutes))
            {
                return HealthCheckResult.Degraded(
                    "Following privacy erasure backlog exceeded its bounded age objective.",
                    data: new Dictionary<string, object>
                    {
                        ["oldestPendingAgeSeconds"] =
                            Math.Max(0, (nowUtc - NormalizeUtc(oldest.Value)).TotalSeconds),
                    });
            }

            return HealthCheckResult.Healthy("Following privacy erasure backlog is within budget.");
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
