using MessageBrokerCore.Abstractions;
using MessageBrokerCore.Messages;
using MessageBrokerRabbitMq.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Net.Sockets;
using System.Text;

namespace MessageBrokerRabbitMq
{
    public class MessageBrokerRabbitMQPublisher : IMessageBrokerPublisher
    {
        private readonly IRabbitMQPersistentConnection _persistentConnection;
        private readonly int _retryCount;
        private readonly ILogger<MessageBrokerRabbitMQPublisher> _logger;

        public MessageBrokerRabbitMQPublisher(
            IServiceProvider provider,
            int retryCount)
        {
            _persistentConnection = provider.GetRequiredService<IRabbitMQPersistentConnection>();
            _logger = provider.GetRequiredService<ILogger<MessageBrokerRabbitMQPublisher>>();
            _retryCount = retryCount;
        }

        public Guid Publish(
            IntegrationMessage message,
            string routingKey,
            string exchange,
            IDictionary<string, object>? args = null)
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryAndConnect();

            var policy = RetryPolicy.Handle<BrokerUnreachableException>()
                .Or<SocketException>()
                .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogInformation(
                       "Could not publish Message: {@message.Id} after {time.TotalSeconds:n1} ({ex.Message})",
                       message.Id,
                       time.TotalSeconds,
                       ex.Message);
                });

            _logger.LogInformation(
                    "Creating RabbitMQ channel to publish message: {@message.Id}",
                    message.Id);

            try
            {
                using (var channel = _persistentConnection.CreateModel())
                {
                    _logger.LogInformation(
                      "Declaring RabbitMQ exchange to publish message: {@message.Id}",
                      message.Id);

                    var serializedMessage = JsonConvert.SerializeObject(message);
                    var body = Encoding.UTF8.GetBytes(serializedMessage);

                    policy.Execute(() =>
                    {
                        var properties = channel.CreateBasicProperties();
                        properties.DeliveryMode = 2;

                        if (!(args is null))
                            properties.Headers = args;

                        _logger.LogInformation(
                            "{@routingKey} Publishing message to RabbitMQ: {@message.Id}",
                            message.Id,
                            routingKey );

                        channel.BasicPublish(
                            exchange: exchange,
                            routingKey: routingKey,
                            mandatory: true,
                            basicProperties: properties,
                            body: body);
                    });

                    return message.Id.GetValueOrDefault();
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                        "Publish Process fail,error:{ex.Message},stackTrace:{ex.StackTrace},message:{message}",
                        ex.Message,
                        ex.StackTrace,
                        message);

                _logger.LogError(-1, ex, "Publish Process fail");
            }

            return default;
        }
    }


}
