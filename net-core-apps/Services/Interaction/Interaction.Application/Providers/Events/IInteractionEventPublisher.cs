using Interaction.Contracts;
using MessageBrokerContracts.Privacy.Restricted;

namespace Interaction.Application.Providers.Events;

public interface IInteractionEventPublisher
{
    Task PublishReactionAsync(ReactionChanged message, CancellationToken cancellationToken);
    Task PublishRatingAsync(RatingChanged message, CancellationToken cancellationToken);
    Task PublishSummaryAsync(EngagementSummaryChanged message, CancellationToken cancellationToken);
    Task PublishErasureCompletedAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken);
}
