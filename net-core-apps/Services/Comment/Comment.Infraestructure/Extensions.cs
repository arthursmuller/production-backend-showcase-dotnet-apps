using Comment.Infra.Database;
using Comment.Infra.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Comment.Infra
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
