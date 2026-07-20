using Following.Infra.Persistence;
using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Following.Infra.Database;

public static class FollowingDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = MariaDbConnectionPolicy.Create(configuration, "following");
        services.AddMariaDbCommandTelemetry(configuration);

        services.AddDbContextPool<FollowingContext>((provider, options) =>
        {
            options.UseMariaDbRuntime(connection, provider);
        },
            poolSize: 128);

        return services;
    }
}
