using Interaction.Domain.AggregatesModel;

namespace Interaction.Domain.Repositories;

public sealed record PublicSummaryState(ResourceState? State, EngagementSummary? Summary);

public sealed record ProjectionGapBacklogState(
    long PendingCount,
    DateTimeOffset? OldestReceivedAtUtc);

public sealed record ViewerInteractionState(
    ResourceRef Resource,
    bool IsAvailable,
    ReactionKind? Reaction,
    byte? Rating,
    long? ReactionVersion,
    long? RatingVersion);

public interface IInteractionTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IInteractionRepository
{
    ValueTask<IInteractionTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    ValueTask<IInteractionTransaction> BeginReadCommittedAsync(CancellationToken cancellationToken);
    Task<ResourceState?> GetResourceStateAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken);
    Task<bool> IsUserErasedAsync(long userId, CancellationToken cancellationToken);
    Task<ResourceStateGap?> GetResourceGapAsync(ResourceRef resource, long sourceVersion, CancellationToken cancellationToken);
    Task<ProjectionGapBacklogState> GetProjectionGapBacklogAsync(CancellationToken cancellationToken);
    void AddResourceState(ResourceState state);
    void AddResourceGap(ResourceStateGap gap);
    void RemoveResourceGap(ResourceStateGap gap);
    Task<Reaction?> GetReactionAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken);
    void AddReaction(Reaction reaction);
    void RemoveReaction(Reaction reaction);
    Task<Rating?> GetRatingAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken);
    void AddRating(Rating rating);
    void RemoveRating(Rating rating);
    Task<EngagementSummary?> GetSummaryAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken);
    void AddSummary(EngagementSummary summary);
    Task<PublicSummaryState> GetPublicSummaryStateAsync(ResourceRef resource, CancellationToken cancellationToken);
    Task<bool> InsertViewObservationAsync(ViewObservation observation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ViewObservation>> LockPendingViewsAsync(
        DateTimeOffset notExpiredAtUtc,
        int maximum,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EngagementSummary>> LockSummariesForReconciliationAsync(int maximum, CancellationToken cancellationToken);
    Task<(long Likes, long Dislikes)> CountReactionsAsync(ResourceRef resource, CancellationToken cancellationToken);
    Task<(long Count, long Total)> AggregateRatingsAsync(ResourceRef resource, CancellationToken cancellationToken);
    Task<IReadOnlyList<ViewerInteractionState>> GetViewerStatusesAsync(
        long actorId,
        IReadOnlyList<ResourceRef> resources,
        CancellationToken cancellationToken);
    Task<int> DeleteExpiredViewObservationsAsync(DateTimeOffset now, int maximum, CancellationToken cancellationToken);
    Task<int> PurgeResourcePersonalDataAsync(ResourceRef resource, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
