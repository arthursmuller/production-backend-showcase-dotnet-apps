using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using CategoriesProvider.Abstractions;
using CategoriesProvider.Providers.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HttpClientHelper;
using System;
using CategoriesProvider.Providers.Api;
using AuthenticationProvider;

namespace CategoriesProvider
{
    public static class CategoriesProviderExtensions
    {
        public static IServiceCollection ConfigureCategoriesProvider(this IServiceCollection services, 
                                                                    IConfiguration configuration,
                                                                    ClientCredentialTokenConfig clientTokenConfig = null)
        {
            ConfigureRabbit(services, configuration);
            services.ConfigureAuthenticationProvider(configuration);
            ConfigureApi(services, clientTokenConfig);
            services.AddScoped<ICategoriesService, CategoriesService>();
            return services;
        }

        private static void ConfigureApi(IServiceCollection services, ClientCredentialTokenConfig clientTokenConfig = null)
        {
            var url = GetSetting("CATEGORIES_SERVICE_URL");

            if (!string.IsNullOrEmpty(url))
            {
                services.AddSingleton(clientTokenConfig ?? new ClientCredentialTokenConfig());
                
                HttpProvider.New<ICategoriesApi, CategoriesApiProvider>(services, url);
            }
        }

        private static IServiceCollection ConfigureRabbit(IServiceCollection services, IConfiguration configuration)
        {
            services.RMQConfigureConnection(configuration);
            services.AddSingleton<IBroker, CategoriesProviderRabbitMqProvider>(sp =>
            {
                var connection = sp.GetRequiredService<PublicRmqConnectionConfiguration>();
                return new CategoriesProviderRabbitMqProvider(sp, connection.RetryCount);
            });

            return services;
        }

        private static string GetSetting(string setting) =>
            Environment.GetEnvironmentVariable(setting);
    }
}
