using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class AudienceProjectionHealthCheck(
        FeedContext context,
        IOptions<AudienceProjectionOptions> options,
        TimeProvider timeProvider,
        AudienceProjectionTelemetry telemetry) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext healthContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var bootstrap = await context.FeedAudienceBootstrapStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == FeedAudienceBootstrapState.SingletonId,
                        cancellationToken);
                if (bootstrap is null)
                {
                    telemetry.RecordBootstrap("health_missing");
                    return HealthCheckResult.Unhealthy(
                        "Feed audience bootstrap has never completed.");
                }

                // Projection gaps also degrade the bootstrap singleton. Diagnose them before
                // the generic bootstrap status so readiness and alerts retain the actionable
                // failure cause.
                var hasGap = await context.FeedRelationshipCheckpoints
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.Status == AudienceProjectionStates.Gap
                            || item.Status == AudienceProjectionStates.Quarantined,
                        cancellationToken);
                var hasUnresolvedQuarantine = await context.FeedRelationshipEventQuarantines
                    .AsNoTracking()
                    .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken);
                if (hasGap || hasUnresolvedQuarantine)
                {
                    telemetry.RecordBootstrap("health_gapped");
                    return HealthCheckResult.Unhealthy(
                        "Feed audience projection has an unresolved relationship gap.",
                        data: new Dictionary<string, object>
                        {
                            ["gapCheckpoint"] = hasGap,
                            ["unresolvedQuarantine"] = hasUnresolvedQuarantine,
                        });
                }

                if (!bootstrap.IsCompleteAndFresh(
                    now,
                    TimeSpan.FromSeconds(options.Value.MaximumBootstrapAgeSeconds)))
                {
                    var reason = bootstrap.Status == AudienceBootstrapStates.Healthy
                        ? "stale"
                        : "incomplete";
                    telemetry.RecordBootstrap($"health_{reason}");
                    return HealthCheckResult.Unhealthy(
                        $"Feed audience bootstrap is {reason}.",
                        data: new Dictionary<string, object>
                        {
                            ["status"] = bootstrap.Status,
                            ["cycleInProgress"] = bootstrap.CycleInProgress,
                            ["lastCompletedAtUtc"] = bootstrap.LastCompletedAtUtc?.ToString("O")
                                ?? string.Empty,
                            ["lastErrorCode"] = bootstrap.LastErrorCode ?? string.Empty,
                        });
                }

                var cutoff = now.AddSeconds(
                    -options.Value.MaximumCheckpointAgeSeconds);
                var hasStaleCheckpoint = await context.FeedRelationshipCheckpoints
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.Status == AudienceProjectionStates.Healthy
                            && item.LastConfirmedAtUtc < cutoff,
                        cancellationToken);
                if (hasStaleCheckpoint)
                {
                    telemetry.RecordBootstrap("health_stale_checkpoint");
                    return HealthCheckResult.Unhealthy(
                        "Feed audience projection contains stale relationship checkpoints.");
                }
                telemetry.RecordBootstrap("health_healthy");
                return HealthCheckResult.Healthy(
                    "Feed audience bootstrap and relationship checkpoints are healthy.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy(
                    "Feed audience projection health query failed.",
                    exception);
            }
        }
    }
}
