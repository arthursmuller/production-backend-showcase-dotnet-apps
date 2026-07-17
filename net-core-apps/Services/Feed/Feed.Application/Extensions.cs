using Feed.Application.MessageBrokers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Feed.Application
{
    public static class ApplicationExtensions
    {
        public static IServiceCollection ConfigureApplication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddScoped<IMessages, MessagePublisher>();
            return services;
        }
    }
}
