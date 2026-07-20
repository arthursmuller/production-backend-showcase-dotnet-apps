using Interaction.Application.Providers.Events;
using Interaction.Contracts;
using Interaction.Infraestructure.Database.Ef;
using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerExtensions;

namespace Interaction.Infraestructure.Providers.Events;

public sealed class InteractionEventPublisher(
    IPublishEndpoint publishEndpoint,
    InteractionDbContext context)
    : IInteractionEventPublisher
{
    public Task PublishReactionAsync(
        ReactionChanged message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(message, message.EventId, cancellationToken);

    public Task PublishRatingAsync(
        RatingChanged message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(message, message.EventId, cancellationToken);

    public Task PublishSummaryAsync(
        EngagementSummaryChanged message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(message, message.EventId, cancellationToken);

    public Task PublishErasureCompletedAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(message, message.EventId, cancellationToken);

    private async Task PublishStagedAsync<TMessage>(
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        if (messageId == Guid.Empty)
            throw new InvalidOperationException("Interaction messages require a stable non-empty ID.");

        await context.PublishToBusOutboxAsync(
            publishEndpoint,
            message,
            messageId,
            cancellationToken);
    }
}
