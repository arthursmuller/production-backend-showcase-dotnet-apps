using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserProvider.Abstractions;
using UserProvider.Providers.MessageBroker.RabbitMq;

namespace UserProvider
{
    public static class UserProviderExtensions
    {
        public static IServiceCollection ConfigureUserProvider(this IServiceCollection services, IConfiguration configuration)
        {
            ConfigureRabbit(services, configuration);
            services.AddScoped<IUserService, UserService>();

            return services;
        }

        private static void ConfigureRabbit(IServiceCollection services, IConfiguration configuration)
        {
            services.RMQConfigureConnection(configuration);
            services.AddSingleton<IBroker, UserRabbitMqProvider>(sp =>
            {
                var connection = sp.GetRequiredService<PublicRmqConnectionConfiguration>();
                return new UserRabbitMqProvider(sp, connection.RetryCount);
            });
        }
    }
}
