namespace Moderation.Domain.Repositories;

public sealed record ModerationRetentionBatchResult(
    bool LeaseAcquired,
    int CasesRedacted,
    int CasesPurged,
    long FencingToken);

public interface IModerationRetentionStore
{
    Task<ModerationRetentionBatchResult> ExecuteBatchAsync(
        DateTimeOffset redactBeforeUtc,
        DateTimeOffset purgeBeforeUtc,
        DateTimeOffset nowUtc,
        string leaseOwner,
        TimeSpan leaseDuration,
        int maxBatchSize,
        CancellationToken cancellationToken);
}
