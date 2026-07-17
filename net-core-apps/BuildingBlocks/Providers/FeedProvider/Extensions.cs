using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FeedProvider.Abstractions;
using FeedProvider.Providers.MessageBroker.RabbitMq;

namespace FeedProvider
{
    public static class FeedProviderExtensions
    {
        public static IServiceCollection ConfigureFeedProvider(this IServiceCollection services, IConfiguration configuration)
        {
            ConfigureRabbit(services, configuration);
            services.AddScoped<IFeedService, FeedService>();

            return services;
        }

        private static void ConfigureRabbit(IServiceCollection services, IConfiguration configuration)
        {
            services.RMQConfigureConnection(configuration);
            services.AddSingleton<IBroker, FeedRabbitMqProvider>(sp =>
            {
                var connection = sp.GetRequiredService<PublicRmqConnectionConfiguration>();
                return new FeedRabbitMqProvider(sp, connection.RetryCount);
            });
        }
    }
}
