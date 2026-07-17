using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationProvider;
using Following.Application.Abstractions;

namespace Following.Infra.Providers.Notifications
{
    public static class NotificationsExtensions
    {
        public static IServiceCollection ConfigureNotifications(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureNotificationProvider(configuration);
            services.AddScoped<INotifications, Notifications>();

            return services;
        }
    }
}

