using Comment.Application.Features.Visibility;
using Comment.Domain.AggregatesModel.Visibility;
using InspacyApiHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Visibility
{
    public sealed class CommentAudienceProjectionHealthCheck(
        CommentContext context,
        IOptions<CommentAudienceProjectionOptions> options,
        TimeProvider timeProvider,
        IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext healthContext, CancellationToken cancellationToken = default)
        {
            var value = options.Value;
            var ownsBootstrap = RuntimeIdentity.Resolve(configuration).RunsJobs();
            if (!value.EnableReadEnforcement && !ownsBootstrap)
                return HealthCheckResult.Healthy("Comment audience projection is warming; read activation remains off.");
            var state = await context.CommentAudienceProjectionStates.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == CommentAudienceProjectionState.SingletonId,
                    cancellationToken);
            if (state is null || !state.IsCompleteAndFresh(timeProvider.GetUtcNow().UtcDateTime,
                TimeSpan.FromSeconds(value.MaximumBootstrapAgeSeconds)))
                return HealthCheckResult.Unhealthy("The Comment relationship bootstrap is missing, incomplete, or stale.");
            if (await context.CommentRelationshipCheckpoints.AsNoTracking()
                .AnyAsync(item => item.Status != CommentProjectionStates.Healthy, cancellationToken))
                return HealthCheckResult.Unhealthy("A Comment relationship checkpoint is unhealthy.");
            if (await context.CommentRelationshipEventQuarantines.AsNoTracking()
                .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken))
                return HealthCheckResult.Unhealthy("A Comment relationship event is quarantined.");
            return HealthCheckResult.Healthy("The Comment relationship projection is complete and fresh.");
        }
    }
}
