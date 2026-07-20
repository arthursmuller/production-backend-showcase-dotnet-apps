namespace Interaction.Domain.Repositories;

public sealed record InteractionAuthorityRevisionRetentionState(
    long EstimatedTotalRevisionCount,
    int EligibleRevisionCountLowerBound,
    bool HasMoreEligibleRevisions,
    DateTimeOffset? OldestSupersededAtUtc);

public interface IInteractionAuthorityRevisionMaintenanceRepository
{
    Task<InteractionAuthorityRevisionRetentionState> GetRetentionStateAsync(
        DateTimeOffset cutoffUtc,
        int maximumSampleSize,
        CancellationToken cancellationToken);

    Task<int> PruneSupersededAsync(
        DateTimeOffset cutoffUtc,
        int maximum,
        CancellationToken cancellationToken);
}
