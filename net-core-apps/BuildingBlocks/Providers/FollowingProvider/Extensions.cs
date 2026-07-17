using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FollowingProvider.Abstractions;
using System;
using HttpClientHelper;
using FollowingProvider.Providers.Api;
using AuthenticationProvider;

namespace FollowingProvider
{
    public static class FollowingProviderExtensions
    {
        public static IServiceCollection ConfigureFollowingProvider(this IServiceCollection services,
                                                                    IConfiguration configuration,
                                                                    ClientCredentialTokenConfig clientTokenConfig)
        {
            services.ConfigureAuthenticationProvider(configuration);
            ConfigureApi(services);
            services.AddScoped<IFollowingService, FollowingService>();
            services.AddSingleton(clientTokenConfig);

            return services;
        }

        private static void ConfigureApi(IServiceCollection services)
        {
            var url = GetSetting("FOLLOWING_SERVICE_URL_PATH");

            if (!string.IsNullOrEmpty(url))
                HttpProvider.New<IFollowingApi, FollowingApiProvider>(services, url);
        }

        private static string GetSetting(string setting) => Environment.GetEnvironmentVariable(setting);
    }
}
