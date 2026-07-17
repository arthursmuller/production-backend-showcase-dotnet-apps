using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Posts.Provider.Abstractions;
using Posts.Provider.Providers.RabbitMq;

namespace Posts.Provider
{
    public static class PostsProviderExtensions
    {
        public static IServiceCollection ConfigurePostsProvider(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.RMQConfigureConnection(configuration);
            services.AddSingleton<IBroker, PostsRabbitMqProvider>(serviceProvider =>
            {
                var connection = serviceProvider.GetRequiredService<PublicRmqConnectionConfiguration>();
                return new PostsRabbitMqProvider(serviceProvider, connection.RetryCount);
            });
            services.AddScoped<IPostsService, PostsService>();
            return services;
        }
    }
}
