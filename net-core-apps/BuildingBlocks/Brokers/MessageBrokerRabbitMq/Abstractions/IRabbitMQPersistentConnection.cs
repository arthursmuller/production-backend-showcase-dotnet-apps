using RabbitMQ.Client;

namespace MessageBrokerRabbitMq.Abstractions
{
    public interface IRabbitMQPersistentConnection : IDisposable
    {
        bool IsConnected { get; }

        bool TryAndConnect();

        IModel CreateModel();
    }
}
