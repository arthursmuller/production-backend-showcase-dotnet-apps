using AuthenticationProvider;
using CategoriesProvider.Abstractions;
using CategoriesProvider.Providers.Api;
using Following.Application.Providers.Categories;
using Following.Application.Providers.Notifications;
using Following.Infra.Providers.Notifications;
using HttpClientHelper;
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
            services.ConfigureAuthenticationProvider(configuration);
            var categoriesUrl = configuration["Categories:ServiceUrl"];
            if (!string.IsNullOrWhiteSpace(categoriesUrl))
            {
                services.AddSingleton(new ClientCredentialTokenConfig(
                    RequireConfiguration(configuration, "Identity:ClientId"),
                    RequireConfiguration(configuration, "Identity:ClientSecret"),
                    RequireConfiguration(configuration, "Categories:ReadScope")));
                HttpProvider.New<ICategoriesApi, CategoriesApiProvider>(
                    services,
                    categoriesUrl);
            }

            services.AddScoped<ICategories, CategoryAdapter>();
            services.AddScoped<INotifications, FollowingNotificationPublisher>();

            return services;
        }

        private static string RequireConfiguration(
            IConfiguration configuration,
            string key)
        {
            var value = configuration[key];
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException(
                    $"The required configuration value '{key}' is missing.");
        }
    }
}
