using Feed.Infra.Persistence;
using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Feed.Infra.Database;

public static class FeedDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = MariaDbConnectionPolicy.Create(configuration, "feed");
        services.AddMariaDbCommandTelemetry(configuration);

        services.AddDbContextPool<FeedContext>((provider, options) =>
        {
            options.UseMariaDbRuntime(connection, provider);
        },
            poolSize: 128);

        return services;
    }
}
