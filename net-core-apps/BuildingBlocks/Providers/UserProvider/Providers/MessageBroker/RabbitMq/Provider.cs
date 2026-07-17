using System;
using UserProvider.Dtos;
using MessageBrokerRabbitMq;
using MessageBrokerCore.Constants;
using UserProvider.Abstractions;
using UserProvider.Providers.MessageBroker.RabbitMq.Messages;

namespace UserProvider.Providers.MessageBroker.RabbitMq
{
    public class UserRabbitMqProvider: MessageBrokerRabbitMQPublisher, IBroker
    {
        public UserRabbitMqProvider(
            IServiceProvider services,
            int retryCount) : base(services, retryCount)
        { }

        public Guid UpdateAnalytics(UserUpdateAnalytics dto) =>
             Publish(
                 new UserUpdateAnalyticsMessage(dto),
                 Topology.User.UpdateAnalytics.RoutingKey,
                 Topology.User.EventsExchange);
    }
}

