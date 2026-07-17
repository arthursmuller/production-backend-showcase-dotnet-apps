using Feed.Infra.Providers.MessageBrokers;
using FollowingProvider;
using Feed.Application.Abstractions;
using IdentityProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Feed.Infra.Providers
{
    public static class ProvidersExtensions
    {
        public static IServiceCollection ConfigureProviders(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .ConfigureFollowingProvider(configuration,
                                             new(
                                                GetSetting("IDENTITY_SERVICE_CLIENT_ID"),
                                                GetSetting("IDENTITY_SERVICE_CLIENT_SECRET"),
                                                GetSetting("IDENTITY_SERVICE_CLIENT_FOLLOWING_READ_SCOPE")))
                .ConfigureMessageBroker(configuration)
                .ConfigureIdentityProvider();

            services.AddScoped<IFollowingConnections, FollowingConnections>();
            
            return services;
        }

        private static string GetSetting(string setting) => Environment.GetEnvironmentVariable(setting);
    }
}
