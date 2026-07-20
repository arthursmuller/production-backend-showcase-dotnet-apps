using Moderation.Domain.AggregatesModel;

namespace Moderation.Domain.Repositories;

public interface IModerationUserErasureStore
{
    ValueTask<IModerationUserErasureTransaction> BeginSerializableAsync(
        CancellationToken cancellationToken);
    Task<ModerationUserErasure?> GetForUpdateAsync(
        long userId,
        CancellationToken cancellationToken);
    Task<bool> IsUserErasedAsync(
        long userId,
        CancellationToken cancellationToken);
    void Add(ModerationUserErasure erasure);
    Task<ModerationUserErasure?> LockNextDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ModerationErasureDataBatch> ExecuteDataBatchAsync(
        long userId,
        long fencingToken,
        int maximum,
        CancellationToken cancellationToken);
    Task<bool> HasDeletableDataAsync(
        long userId,
        CancellationToken cancellationToken);
    Task<int> CountActiveHeldCasesAsync(
        long userId,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IModerationUserErasureTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record ModerationErasureDataBatch(
    int CasesClaimed,
    int MessagesDeleted,
    int AuditEntriesDeleted,
    int EvidenceReferencesDeleted,
    int ReleasedLegalHoldsDeleted,
    int CasesDeleted)
{
    public int WorkUnits => CasesClaimed
        + MessagesDeleted
        + AuditEntriesDeleted
        + EvidenceReferencesDeleted
        + ReleasedLegalHoldsDeleted
        + CasesDeleted;
}
