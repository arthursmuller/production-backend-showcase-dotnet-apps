using Comment.Application.Features.Visibility;
using Comment.Infra.Providers.Storage;
using InspacyApiHost;
using FollowingProvider;
using IdentityProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Comment.Infra.Providers
{
    public static class ProvidersExtensions
    {
        public static IServiceCollection ConfigureProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services
                .ConfigureIdentityProvider()
                .ConfigureStorage(configuration);

            var runtimeRole = RuntimeIdentity.Resolve(configuration);
            if (runtimeRole.RunsJobs())
            {
                var clientId = configuration["Following:Snapshot:ClientId"];
                var clientSecret = configuration["Following:Snapshot:ClientSecret"];
                var readScope = configuration["Following:Snapshot:ReadScope"];
                if (string.IsNullOrWhiteSpace(configuration["Following:ServiceUrl"])
                    || string.IsNullOrWhiteSpace(clientId)
                    || string.IsNullOrWhiteSpace(clientSecret)
                    || string.IsNullOrWhiteSpace(readScope))
                    throw new InvalidOperationException(
                        "The JobWorker/Combined role requires dedicated Following:Snapshot credentials and ServiceUrl.");
                services.ConfigureFollowingProvider(
                    configuration,
                    new FollowingProvider.Providers.Api.ClientCredentialTokenConfig(
                        clientId,
                        clientSecret,
                        readScope));
                services.AddScoped<ICommentRelationshipSnapshotSource,
                    FollowingRelationshipSnapshotSource>();
            }

            return services;
        }
    }
}
