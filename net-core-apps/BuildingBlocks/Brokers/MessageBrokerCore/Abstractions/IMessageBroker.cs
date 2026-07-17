using MessageBrokerCore.Messages;
using System;
using System.Collections.Generic;

namespace MessageBrokerCore.Abstractions
{
    public interface IMessageBroker
    {
        void Subscribe();
    }

    public interface IMessageBrokerPublisher
    {
        Guid Publish(
            IntegrationMessage message,
            string routingKey,
            string exchange,
            IDictionary<string, object>? args = null);
    }
}

