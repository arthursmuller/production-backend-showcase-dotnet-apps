using System;
using MessageBrokerRabbitMq;
using MessageBrokerCore.Constants;
using FeedProvider.Abstractions;
using FeedProvider.Dtos;
using FeedProvider.Providers.MessageBroker.RabbitMq.Messages;

namespace FeedProvider.Providers.MessageBroker.RabbitMq
{
    public class FeedRabbitMqProvider: MessageBrokerRabbitMQPublisher, IBroker
    {
        public FeedRabbitMqProvider(
            IServiceProvider services,
            int retryCount) : base(services, retryCount)
        { }

        public Guid Add(UserConnectionsFeedDto dto) => Publish(new AddFeedMessage(dto), Topology.Feed.UserConnectionsAdd.RoutingKey, Topology.Feed.EventsExchange);

        public Guid ChangeVisibility(string entityType, string entityId, bool isPublic) => Publish(new ChangeVisibilityMessage(new (entityId, entityType, isPublic)), Topology.Feed.ChangeVisibility.RoutingKey, Topology.Feed.EventsExchange);

        public Guid Remove(string entityType, string entityId) => Publish(new RemoveFeedMessage(new (entityId, entityType)), Topology.Feed.UserConnectionsRemove.RoutingKey, Topology.Feed.EventsExchange);
    }
}

