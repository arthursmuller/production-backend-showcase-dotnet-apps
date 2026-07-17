using MessageBrokerCore.Abstractions;
using MessageBrokerRabbitMqExtensions;
using MessageBrokerRabbitMq;
using MessageBrokerRabbitMq.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;

namespace MessageBrokerRabbitMqExtensions
{
    public static class RabbitMqBrokerExtensions
    {
        public static IServiceCollection RMQConfigureConnection(this IServiceCollection services, IConfiguration configuration)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(IRabbitMQPersistentConnection)))
            {
                var serializedConfiguration = GetSetting("RMQ_CONNECTION", configuration)
                    ?? throw new InvalidOperationException("RMQ_CONNECTION is required when RabbitMQ queues are enabled.");
                var config = JsonConvert.DeserializeObject<RmqConnectionConfig>(serializedConfiguration)
                    ?? throw new InvalidOperationException("RMQ_CONNECTION contains an invalid RabbitMQ configuration.");

                services.AddSingleton(new PublicRmqConnectionConfiguration(config.Exchange, config.Retrycount));

                services.AddSingleton<IRabbitMQPersistentConnection>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<DefaultRabbitMQPersistentConnection>>();

                    var factory = new ConnectionFactory()
                    {
                        HostName = config.Host,
                        UserName = config.Username,
                        Password = config.Pw,
                        VirtualHost = config.Vhost,
                        Port = config.Port,
                        RequestedHeartbeat = TimeSpan.FromSeconds(60)
                    };

                    var isAsync = true;
                    factory.DispatchConsumersAsync = isAsync;

                    return new DefaultRabbitMQPersistentConnection(factory, logger, config.Retrycount ?? 5);
                });
            }

            return services;
        }

        public static void ConfigureConsumers(IApplicationBuilder app)
        {
            // IMessageBroker registrations represent consumers. Publishers use the separate
            // IMessageBrokerPublisher abstraction, so subscribing by implementation name is
            // unnecessary and prevented valid consumers with other naming conventions.
            foreach (var consumer in app.ApplicationServices.GetServices<IMessageBroker>())
                consumer.Subscribe();
        }

        private static string? GetSetting(string setting, IConfiguration configuration) =>
            Environment.GetEnvironmentVariable(setting) ?? configuration[setting];
    }
}
