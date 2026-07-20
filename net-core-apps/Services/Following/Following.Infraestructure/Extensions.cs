using Following.Domain.Repositories;
using Following.Infra.Database;
using Following.Infra.Persistence;
using Following.Infra.Providers;
using Following.Application.Providers.Time;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Privacy;
using IdentityProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Following.Infra
{
    public static class InfraestructureExtensions
    {
        public static IServiceCollection ConfigureInfraestructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigureDb(configuration);
            services.AddScoped<IFollowingRepository, FollowingRepository>();
            services.AddScoped<IFollowingReadRepository, FollowingReadRepository>();
            services.AddScoped<IRelationshipEventWriter, RelationshipEventWriter>();
            services.AddScoped<IRelationshipSnapshotReader, RelationshipSnapshotReader>();
            services.AddScoped<IFollowingUserErasureScheduler, FollowingUserErasureScheduler>();
            services.AddScoped<IFollowingUserErasureProcessor, FollowingUserErasureProcessor>();
            services.AddSingleton<FollowingUserErasureMetrics>();
            services.AddHealthChecks().AddCheck<FollowingUserErasureHealthCheck>(
                "following-user-erasure",
                tags: ["ready", "privacy"]);
            services.AddSingleton<IClock, SystemClock>();
            services.ConfigureIdentityProvider();
            services.ConfigureProviders(configuration);

            return services;
        }
    }
}
