using Interaction.Infraestructure.Database;
using Interaction.Infraestructure.Database.Redis;
using Interaction.Infraestructure.Privacy;
using Interaction.Infraestructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Interaction.Infraestructure;

public static class InfraestructureExtensions
{
    public static IServiceCollection ConfigureInfraestructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .ConfigureDatabase(configuration)
            .ConfigureRedis(configuration)
            .ConfigureProviders()
            .ConfigurePrivacy(configuration);
    }
}
