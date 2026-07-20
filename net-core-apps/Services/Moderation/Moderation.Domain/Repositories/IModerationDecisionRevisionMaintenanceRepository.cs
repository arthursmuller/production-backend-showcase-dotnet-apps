namespace Moderation.Domain.Repositories;

public sealed record ModerationDecisionRevisionRetentionState(
    long EstimatedTotalRevisionCount,
    int EligibleRevisionCountLowerBound,
    bool HasMoreEligibleRevisions,
    DateTimeOffset? OldestSupersededAtUtc);

public interface IModerationDecisionRevisionMaintenanceRepository
{
    Task<ModerationDecisionRevisionRetentionState> GetRetentionStateAsync(
        DateTimeOffset cutoffUtc,
        int maximumSampleSize,
        CancellationToken cancellationToken);

    Task<int> PruneSupersededAsync(
        DateTimeOffset cutoffUtc,
        int maximum,
        CancellationToken cancellationToken);
}
