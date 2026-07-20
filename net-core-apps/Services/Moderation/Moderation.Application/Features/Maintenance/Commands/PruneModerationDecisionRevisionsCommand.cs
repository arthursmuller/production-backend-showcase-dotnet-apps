using Microsoft.Extensions.Options;
using Moderation.Application.Options;
using Moderation.Application.Telemetry;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Maintenance.Commands;

public sealed record ModerationDecisionRevisionRetentionResult(
    long EstimatedTotalRevisionCount,
    int EligibleRevisionCountLowerBound,
    bool HasMoreEligibleRevisions,
    int PrunedRevisionCount,
    double OldestEligibleAgeSeconds,
    DateTimeOffset SampledAtUtc);

public sealed class PruneModerationDecisionRevisionsCommand(
    IModerationDecisionRevisionMaintenanceRepository repository,
    IOptions<ModerationDecisionRevisionRetentionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ModerationDecisionRevisionRetentionResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-settings.RetentionDays);
        var state = await repository.GetRetentionStateAsync(
            cutoff,
            settings.MaxBatchSize,
            cancellationToken);
        var pruned = state.EligibleRevisionCountLowerBound == 0
            ? 0
            : await repository.PruneSupersededAsync(
                cutoff,
                settings.MaxBatchSize,
                cancellationToken);
        var result = new ModerationDecisionRevisionRetentionResult(
            state.EstimatedTotalRevisionCount,
            state.EligibleRevisionCountLowerBound,
            state.HasMoreEligibleRevisions,
            pruned,
            state.OldestSupersededAtUtc is null
                ? 0
                : Math.Max(0, (now - state.OldestSupersededAtUtc.Value).TotalSeconds),
            now);
        ModerationTelemetry.SetDecisionRevisionRetention(result);
        if (pruned > 0)
            ModerationTelemetry.PrunedDecisionRevisions.Add(pruned);
        return result;
    }
}
