using Feed.Application.Abstractions;
using Feed.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Feed.Infra.Database;

public static class FeedDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetSetting("SQL_CONNECTION_STRING", configuration)
            ?? throw new InvalidOperationException("SQL_CONNECTION_STRING is required.");

        services.AddDbContext<FeedContext>(opt =>
        {
            opt.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }, ServiceLifetime.Scoped);

        services.AddScoped<IFeedRepository, FeedRepository>();

        return services;
    }

    private static string? GetSetting(string setting, IConfiguration configuration) =>
        Environment.GetEnvironmentVariable(setting) ?? configuration[setting];
}
