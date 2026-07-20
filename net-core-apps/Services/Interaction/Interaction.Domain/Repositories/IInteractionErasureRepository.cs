using Interaction.Domain.AggregatesModel;

namespace Interaction.Domain.Repositories;

public interface IInteractionErasureRepository
{
    Task<InteractionUserErasure?> GetForUpdateAsync(long userId, CancellationToken cancellationToken);
    void Add(InteractionUserErasure erasure);
    Task<InteractionUserErasure?> LockNextPendingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Reaction>> LockReactionsAsync(
        long userId,
        int maximum,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Rating>> LockRatingsAsync(
        long userId,
        int maximum,
        CancellationToken cancellationToken);
    Task<bool> HasActorDataAsync(long userId, CancellationToken cancellationToken);
}
