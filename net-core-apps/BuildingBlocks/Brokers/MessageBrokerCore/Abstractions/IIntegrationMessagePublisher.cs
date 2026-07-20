namespace MessageBrokerCore.Abstractions;

/// <summary>
/// Transport-neutral asynchronous publication boundary for typed integration contracts.
/// Contract types determine the broker topology.
/// </summary>
public interface IIntegrationMessagePublisher
{
    Task<Guid> PublishAsync<TMessage>(
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class;
}
