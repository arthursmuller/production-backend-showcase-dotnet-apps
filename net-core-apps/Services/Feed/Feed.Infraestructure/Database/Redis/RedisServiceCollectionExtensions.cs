using Inspacy.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Feed.Infra.Database.Redis;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection ConfigureRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";

        return services.AddInspacyHybridCache(
            configuration,
            environmentName,
            "Feed.API");
    }
}
