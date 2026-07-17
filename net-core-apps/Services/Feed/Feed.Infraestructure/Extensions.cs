using Feed.Infra.Database;
using Feed.Infra.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Feed.Infra
{
    public static class InfraestructureExtensions
    {
        public static IServiceCollection ConfigureInfraestructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureDb(configuration);
            services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
            services.ConfigureProviders(configuration);

            return services;
        }
    }
}
