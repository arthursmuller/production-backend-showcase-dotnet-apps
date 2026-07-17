using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Following.Infra.Database;

public static class FollowingDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetSetting("SQL_CONNECTION_STRING", configuration)
            ?? throw new InvalidOperationException("SQL_CONNECTION_STRING is required.");

        services.AddDbContext<FollowingContext>(opt =>
        {
            opt.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }, ServiceLifetime.Scoped);

        return services;
    }

    private static string? GetSetting(string setting, IConfiguration configuration) =>
        Environment.GetEnvironmentVariable(setting) ?? configuration[setting];
}
