using Feed.Application.Features.Audience;
using InspacyApiHost;
using AuthenticationProvider;
using IdentityProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace Feed.Infra.Providers
{
    public static class ProvidersExtensions
    {
        public static IServiceCollection ConfigureProviders(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .ConfigureAuthenticationProvider(configuration)
                .ConfigureIdentityProvider();

            var snapshotOptions = services.AddOptions<FollowingSnapshotClientOptions>()
                .Bind(configuration.GetSection(FollowingSnapshotClientOptions.SectionName))
                .Configure(options =>
                {
                    if (string.IsNullOrWhiteSpace(options.ServiceUrl))
                    {
                        options.ServiceUrl = configuration["Following:ServiceUrl"]
                            ?? string.Empty;
                    }
                })
                .Validate(
                    FollowingSnapshotClientOptions.IsValid,
                    "Following:Snapshot requires an absolute service URL, dedicated credentials, a scope, and a bounded timeout.");
            if (RuntimeIdentity.Resolve(configuration).RunsJobs())
            {
                snapshotOptions.ValidateOnStart();
            }
            services.AddHttpClient<FollowingRelationshipBootstrapSource>((provider, client) =>
            {
                var configured = provider
                    .GetRequiredService<IOptions<FollowingSnapshotClientOptions>>()
                    .Value;
                client.BaseAddress = new Uri(configured.ServiceUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(configured.RequestTimeoutSeconds);
            });
            services.AddScoped<IRelationshipBootstrapSource>(provider =>
                provider.GetRequiredService<FollowingRelationshipBootstrapSource>());
            return services;
        }

    }
}
