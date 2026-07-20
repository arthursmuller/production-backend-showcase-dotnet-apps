using Comment.Application.Features.Interactions;
using Comment.Application.Features.Privacy;
using Comment.Domain.Repositories;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Privacy;
using Comment.Infra.Persistence.Repositories;
using Comment.Infra.Persistence.Repositories.Interactions;
using Comment.Infra.Persistence.Repositories.Media;
using Comment.Infra.Persistence.Repositories.Moderation;
using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Comment.Infra.Database;

public static class CommentDatabaseExtensions
{
    public static IServiceCollection ConfigureDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = MariaDbConnectionPolicy.Create(configuration, "comment");
        services.AddMariaDbCommandTelemetry(configuration);

        services.AddDbContext<CommentContext>((provider, options) =>
        {
            options.UseMariaDbRuntime(connection, provider);
        }, ServiceLifetime.Scoped);

        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<ICommentReadRepository, CommentReadRepository>();
        services.AddScoped<ICommentInteractionProjectionStore,
            CommentInteractionProjectionStore>();
        services.AddScoped<ICommentModerationDecisionRepository,
            CommentModerationDecisionRepository>();
        services.AddScoped<ICommentMediaIngestionRepository,
            CommentMediaIngestionRepository>();
        services.AddScoped<CommentUserErasureAuthority>();
        services.AddScoped<ICommentUserErasureAuthority>(provider =>
            provider.GetRequiredService<CommentUserErasureAuthority>());
        services.AddScoped<ICommentUserErasureScheduler, CommentUserErasureScheduler>();
        services.AddHealthChecks().AddCheck<CommentUserErasureHealthCheck>(
            "comment-user-erasure",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]);

        return services;
    }
}
