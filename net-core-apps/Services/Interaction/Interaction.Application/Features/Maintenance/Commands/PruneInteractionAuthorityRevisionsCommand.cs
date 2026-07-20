using Interaction.Application.Options;
using Interaction.Application.Telemetry;
using Interaction.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace Interaction.Application.Features.Maintenance.Commands;

public sealed record InteractionAuthorityRevisionRetentionResult(
    long EstimatedTotalRevisionCount,
    int EligibleRevisionCountLowerBound,
    bool HasMoreEligibleRevisions,
    int PrunedRevisionCount,
    double OldestEligibleAgeSeconds,
    DateTimeOffset SampledAtUtc);

public sealed class PruneInteractionAuthorityRevisionsCommand(
    IInteractionAuthorityRevisionMaintenanceRepository repository,
    IOptions<InteractionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<InteractionAuthorityRevisionRetentionResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-settings.AuthorityRevisionRetentionDays);
        var state = await repository.GetRetentionStateAsync(
            cutoff,
            settings.AuthorityRevisionPruneBatchSize,
            cancellationToken);
        var pruned = state.EligibleRevisionCountLowerBound == 0
            ? 0
            : await repository.PruneSupersededAsync(
                cutoff,
                settings.AuthorityRevisionPruneBatchSize,
                cancellationToken);
        var result = new InteractionAuthorityRevisionRetentionResult(
            state.EstimatedTotalRevisionCount,
            state.EligibleRevisionCountLowerBound,
            state.HasMoreEligibleRevisions,
            pruned,
            state.OldestSupersededAtUtc is null
                ? 0
                : Math.Max(0, (now - state.OldestSupersededAtUtc.Value).TotalSeconds),
            now);
        InteractionTelemetry.SetAuthorityRevisionRetention(result);
        if (pruned > 0)
            InteractionTelemetry.PrunedAuthorityRevisions.Add(pruned);
        return result;
    }
}
