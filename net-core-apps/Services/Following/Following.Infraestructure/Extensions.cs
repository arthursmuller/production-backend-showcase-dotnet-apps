using Following.Application.Abstractions;
using Following.Infra.Database;
using Following.Infra.Persistence;
using Following.Infra.Providers;
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
            services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
            services.AddScoped<IFollowingRepository, FollowingRepository>();
            services.ConfigureIdentityProvider();
            services.ConfigureProviders(configuration);

            return services;
        }
    }
}
