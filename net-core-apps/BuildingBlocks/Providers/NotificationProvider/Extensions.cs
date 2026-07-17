using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationProvider.Abstractions;
using NotificationProvider.Providers.MessageBroker.RabbitMq;
namespace NotificationProvider;

public static class NotificationProviderExtensions
{
    public static IServiceCollection ConfigureNotificationProvider(this IServiceCollection services, IConfiguration configuration)
    {
        ConfigureRabbit(services, configuration);
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    private static void ConfigureRabbit(IServiceCollection services, IConfiguration configuration)
    {
        services.RMQConfigureConnection(configuration);
         
        services.AddSingleton<IBroker, NotificationRabbitMqProvider>(sp =>
        {
            var rmqConnConfig = sp.GetRequiredService<PublicRmqConnectionConfiguration>();

            return new NotificationRabbitMqProvider(sp, rmqConnConfig.RetryCount);
        });
    }
}
