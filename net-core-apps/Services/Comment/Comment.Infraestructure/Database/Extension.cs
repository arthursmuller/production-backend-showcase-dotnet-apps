using Comment.Application.Abstractions.Persistence;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Comment.Infra.Database;

public static class CommentDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetSetting("SQL_CONNECTION_STRING", configuration)
            ?? throw new InvalidOperationException("SQL_CONNECTION_STRING is required.");

        services.AddDbContext<CommentContext>(opt =>
        {
            opt.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }, ServiceLifetime.Scoped);

        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<ICommentSocialRepository, CommentSocialRepository>();

        return services;
    }

    private static string? GetSetting(string setting, IConfiguration configuration) =>
        Environment.GetEnvironmentVariable(setting) ?? configuration[setting];
}
