using Comment.Application.Features.Privacy;
using Comment.Domain.AggregatesModel.Privacy;
using InspacyApiHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Privacy;

public sealed class CommentUserErasureHealthCheck(
    CommentContext context,
    IOptions<CommentUserErasureOptions> options,
    IConfiguration configuration,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        var blocked = await context.CommentUserErasures.AsNoTracking()
            .AnyAsync(value => value.Status == CommentUserErasureStatus.Blocked, cancellationToken);
        if (blocked)
            return HealthCheckResult.Unhealthy("A Comment user erasure is blocked on safe media ownership evidence.");

        var role = RuntimeIdentity.Resolve(configuration);
        if (!role.RunsJobs())
            return HealthCheckResult.Healthy("Comment erasure tombstone enforcement is available.");

        var threshold = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(
            -options.Value.MaximumPendingAgeMinutes);
        var stale = await context.CommentUserErasures.AsNoTracking().AnyAsync(value =>
            value.Status != CommentUserErasureStatus.Completed
            && value.UpdatedAtUtc < threshold, cancellationToken);
        return stale
            ? HealthCheckResult.Unhealthy("Comment user erasure work exceeded its bounded pending age.")
            : HealthCheckResult.Healthy("Comment user erasure work is within its bounded age.");
    }
}
