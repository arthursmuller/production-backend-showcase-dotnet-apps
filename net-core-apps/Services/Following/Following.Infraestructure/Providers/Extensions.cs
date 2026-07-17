using CategoriesProvider;
using CategoriesProvider.Providers.Api;
using Following.Application.Abstractions;
using Following.Infra.Providers.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Following.Infra.Providers
{
    public static class ProvidersExtensions
    {
        public static IServiceCollection ConfigureProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services
                .ConfigureCategoriesProvider(
                    configuration,
                    new ClientCredentialTokenConfig(
                        GetSetting("IDENTITY_SERVICE_CLIENT_ID"),
                        GetSetting("IDENTITY_SERVICE_CLIENT_SECRET"),
                        GetSetting("IDENTITY_SERVICE_CLIENT_CATEGORIES_READ_SCOPE")))
                .ConfigureNotifications(configuration);

            services.AddScoped<IIdentity, IdentityAdapter>();
            services.AddScoped<ICategories, CategoryAdapter>();

            return services;
        }

        private static string GetSetting(string setting) =>
            Environment.GetEnvironmentVariable(setting);
    }
}
