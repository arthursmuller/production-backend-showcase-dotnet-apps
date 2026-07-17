using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthenticationProvider.Abstractions;
using System;
using HttpClientHelper;
using AuthenticationProvider.Providers.Api;

namespace AuthenticationProvider
{
    public static class AuthenticationProviderExtensions
    {
        public static IServiceCollection ConfigureAuthenticationProvider(this IServiceCollection services, IConfiguration configuration)
        {
            ConfigureApi(services);
            services.AddScoped<IAuthService, AuthenticationService>();

            return services;
        }

        private static void ConfigureApi(IServiceCollection services)
        {
            var url = GetSetting("IDENTITY_SERVICE_URL");

            if (!string.IsNullOrEmpty(url))
                HttpProvider.New<IAuthApi, AuthenticationApiProvider>(services, url);
        }

        private static string GetSetting(string setting) => Environment.GetEnvironmentVariable(setting);
    }
}
