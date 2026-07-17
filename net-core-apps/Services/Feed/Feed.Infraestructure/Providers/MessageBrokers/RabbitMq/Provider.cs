using System;
using MessageBrokerRabbitMq;

namespace Feed.Infra.Providers.MessageBrokers.RabbitMq
{
    public sealed class RabbitMqProvider : MessageBrokerRabbitMQPublisher
    {
        public RabbitMqProvider(
            IServiceProvider services,
            int retryCount) : base(services, retryCount)
        { }
    }
}
