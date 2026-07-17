using Feed.Application.Dtos;
using Feed.Application.MessageBrokers.Messages;
using MessageBrokerCore.Abstractions;
using MessageBrokerCore.Constants;
using System;

namespace Feed.Application.MessageBrokers
{
    public interface IMessages
    {
        Guid AddFeedItems(AddFeedItemsDto dto);
        Guid RemoveFeedItems(int postId);
    }

    public sealed class MessagePublisher(IMessageBrokerPublisher publisher) : IMessages
    {
        public Guid AddFeedItems(AddFeedItemsDto dto) =>
            publisher.Publish(
                new AddFeedItemsMessage(dto),
                Topology.Feed.AddItems.RoutingKey,
                Topology.Feed.EventsExchange);

        public Guid RemoveFeedItems(int postId) =>
            publisher.Publish(
                new RemoveFeedItemsMessage(postId),
                Topology.Feed.RemoveItems.RoutingKey,
                Topology.Feed.EventsExchange);
    }
}
