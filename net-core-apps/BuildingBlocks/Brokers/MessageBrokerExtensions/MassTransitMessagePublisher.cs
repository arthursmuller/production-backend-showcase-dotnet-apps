using MassTransit;
using MessageBrokerCore.Abstractions;

namespace MessageBrokerExtensions;

public sealed class MassTransitMessagePublisher(IPublishEndpoint endpoint)
    : IIntegrationMessagePublisher
{
    public async Task<Guid> PublishAsync<TMessage>(
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        if (messageId == Guid.Empty)
            throw new ArgumentException("A non-empty message ID is required.", nameof(messageId));
        await endpoint.Publish(
            message,
            context => context.MessageId = messageId,
            cancellationToken);

        return messageId;
    }
}
