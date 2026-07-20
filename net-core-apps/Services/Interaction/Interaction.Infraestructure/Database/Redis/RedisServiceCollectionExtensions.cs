using Inspacy.Caching;
using Interaction.Application.Providers.Cache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Interaction.Infraestructure.Database.Redis;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection ConfigureRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";

        services.AddInspacyHybridCache(
            configuration,
            environmentName,
            "Interaction.API");
        services.AddSingleton<IEngagementSummaryCache, EngagementSummaryCache>();
        return services;
    }
}
