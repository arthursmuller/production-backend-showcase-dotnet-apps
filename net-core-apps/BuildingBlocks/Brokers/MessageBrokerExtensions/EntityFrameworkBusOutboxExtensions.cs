using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MessageBrokerExtensions;

/// <summary>
/// Guards integration-message publication through MassTransit's EF Core bus outbox.
/// </summary>
public static class EntityFrameworkBusOutboxExtensions
{
    public static async Task<Guid> PublishToBusOutboxAsync<TMessage>(
        this DbContext context,
        IIntegrationMessagePublisher publisher,
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(message);

        var publishedMessageId = await publisher.PublishAsync(
            message,
            messageId,
            cancellationToken);

        return context.EnsureBusOutboxMessageStaged(publishedMessageId);
    }

    public static async Task<Guid> PublishToBusOutboxAsync<TMessage>(
        this DbContext context,
        IPublishEndpoint publisher,
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(message);

        await publisher.Publish(
            message,
            pipe => pipe.MessageId = messageId,
            cancellationToken);

        return context.EnsureBusOutboxMessageStaged(messageId);
    }

    /// <summary>
    /// Verifies that exactly one newly tracked MassTransit outbox row has the expected message ID.
    /// </summary>
    public static Guid EnsureBusOutboxMessageStaged(
        this DbContext context,
        Guid messageId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (messageId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "An integration message requires a stable non-empty ID.");
        }

        var stagedCount = context.ChangeTracker
            .Entries<OutboxMessage>()
            .Count(entry =>
                entry.State == EntityState.Added
                && entry.Entity.MessageId == messageId);
        if (stagedCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one MassTransit bus-outbox row for integration " +
                $"message '{messageId}', but found {stagedCount}. Direct or duplicate " +
                "publishing is disabled.");
        }

        return messageId;
    }
}
