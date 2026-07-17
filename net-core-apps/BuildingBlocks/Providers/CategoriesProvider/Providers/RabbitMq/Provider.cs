using MessageBrokerRabbitMq;
using MessageBrokerCore.Constants;
using CategoriesProvider.Providers.RabbitMq.Messages;
using System;
using CategoriesProvider.Abstractions;
using CategoriesProvider.Dtos;

namespace CategoriesProvider.Providers.RabbitMq
{
    public class CategoriesProviderRabbitMqProvider : MessageBrokerRabbitMQPublisher, IBroker
    {
        public CategoriesProviderRabbitMqProvider(
            IServiceProvider services,
            int retryCount) : base(services, retryCount) { }

        public Guid UpdateFollow(UpdateCategoryFollowsDto dto) => Publish(
            new UpdateCategoryFollowsMessage(dto), 
            Topology.Categories.UpdateFollow.RoutingKey, 
            Topology.Categories.EventsExchange
        );
        public Guid? UpdateMentions(UpdateCategoryMentionsDto dto)
        {
            if (dto.NamesToAdd?.Length > 0 || dto.NamesToRemove?.Length > 0)
                return Publish(new UpdateCategoryMentionsMessage(dto), Topology.Categories.Mentions.RoutingKey, Topology.Categories.EventsExchange);

            return null;
        }
    }
}