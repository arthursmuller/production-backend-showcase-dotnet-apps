using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MessageBrokerCore.Abstractions;
using MessageBrokerRabbitMq;

namespace Feed.Infra.Providers.MessageBrokers.RabbitMq
{
    public static class RabbitMqExtensions
    {
        public static IServiceCollection ConfigureRabbitMq(this IServiceCollection services, IConfiguration configuration)
        {
            services.RMQConfigureConnection(configuration);
            services.AddSingleton<IMessageBrokerPublisher, RabbitMqProvider>(sp =>
            {
                var connection = sp.GetRequiredService<PublicRmqConnectionConfiguration>();
                return new RabbitMqProvider(sp, connection.RetryCount);
            });

            return services;
        }
    }
}
