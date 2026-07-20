using Feed.Infra.Database;
using Feed.Infra.Providers;
using Feed.Infra.Database.Redis;
using Feed.Application.Features.Audience;
using Feed.Application.Features.Engagement;
using Feed.Application.Features.Privacy;
using Feed.Application.Features.Feeds;
using Feed.Infra.Persistence;
using Feed.Infra.Persistence.Moderation;
using Feed.Domain.Repositories;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Feed.Infra
{
    public static class InfraestructureExtensions
    {
        public static IServiceCollection ConfigureInfraestructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureDb(configuration);
            services.ConfigureRedis(configuration);
            services.ConfigureProviders(configuration);
            services.AddOptions<AudienceProjectionOptions>()
                .Bind(configuration.GetSection(AudienceProjectionOptions.SectionName))
                .Validate(
                    options => options.MaximumBootstrapPageSize is >= 1 and <= 500,
                    "AudienceProjection:MaximumBootstrapPageSize must be between 1 and 500.")
                .Validate(
                    options => options.MaximumQuarantineDrain is >= 1 and <= 500,
                    "AudienceProjection:MaximumQuarantineDrain must be between 1 and 500.")
                .Validate(
                    options => options.MaximumCheckpointAgeSeconds is >= 60 and <= 604800,
                    "AudienceProjection:MaximumCheckpointAgeSeconds must be between 60 and 604800.")
                .Validate(
                    options => options.MaximumBootstrapAgeSeconds is >= 60 and <= 86400,
                    "AudienceProjection:MaximumBootstrapAgeSeconds must be between 60 and 86400.")
                .Validate(
                    options => options.BootstrapIntervalSeconds is >= 10 and <= 86400,
                    "AudienceProjection:BootstrapIntervalSeconds must be between 10 and 86400.")
                .Validate(
                    options => options.BootstrapRetrySeconds is >= 1 and <= 3600,
                    "AudienceProjection:BootstrapRetrySeconds must be between 1 and 3600.")
                .Validate(
                    options => options.BootstrapPageDelayMilliseconds is >= 10 and <= 5000,
                    "AudienceProjection:BootstrapPageDelayMilliseconds must be between 10 and 5000.")
                .Validate(
                    options => options.BootstrapLeaseSeconds is >= 5 and <= 300,
                    "AudienceProjection:BootstrapLeaseSeconds must be between 5 and 300.")
                .Validate(
                    options => options.AuthorizationDeadlineMilliseconds is >= 50 and <= 2000,
                    "AudienceProjection:AuthorizationDeadlineMilliseconds must be between 50 and 2000.")
                .Validate(
                    options => options.MaximumFriendsOfFriendsCandidates is >= 1 and <= 500,
                    "AudienceProjection:MaximumFriendsOfFriendsCandidates must be between 1 and 500.")
                .Validate(
                    options => options.MaximumFriendsOfFriendsFanoutBatchSize is >= 1 and <= 500,
                    "AudienceProjection:MaximumFriendsOfFriendsFanoutBatchSize must be between 1 and 500.")
                .ValidateOnStart();
            services.AddOptions<FeedOptions>()
                .Bind(configuration.GetSection(FeedOptions.SectionName))
                .Validate(
                    FeedOptionsValidation.IsValid,
                    "Feed contains an unsafe value or exceeds a hard scaling bound.")
                .ValidateOnStart();
            services.AddOptions<FeedUserErasureOptions>()
                .Bind(configuration.GetSection(FeedUserErasureOptions.SectionName))
                .Validate(
                    FeedUserErasureOptions.IsValid,
                    "FeedUserErasure contains an unsafe value or exceeds a hard scaling bound.")
                .ValidateOnStart();
            var cursorOptions = services.AddOptions<FeedCursorOptions>()
                .Bind(configuration.GetSection(FeedCursorOptions.SectionName))
                .Validate(
                    FeedCursorOptionsValidation.IsValid,
                    "FeedCursor requires a valid active key and 32-byte-or-longer base64 HMAC key.");
            if (RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
            {
                cursorOptions.ValidateOnStart();
            }
            services.AddSingleton<AudienceProjectionTelemetry>();
            services.AddSingleton<FeedTelemetry>();
            services.AddSingleton<FeedUserErasureMetrics>();
            services.AddScoped<FeedUserErasureAuthority>();
            services.AddScoped<IFeedUserErasureAuthority>(provider =>
                provider.GetRequiredService<FeedUserErasureAuthority>());
            services.AddScoped<IFeedUserErasureScheduler, FeedUserErasureScheduler>();
            services.AddScoped<IFeedUserErasureProcessor, FeedUserErasureProcessor>();
            services.AddScoped<AudienceProjectionStore>();
            services.AddScoped<IRelationshipProjectionWriter>(provider =>
                provider.GetRequiredService<AudienceProjectionStore>());
            services.AddScoped<IRelationshipProjectionReconciler>(provider =>
                provider.GetRequiredService<AudienceProjectionStore>());
            services.AddScoped<IAudienceProjectionReader>(provider =>
                provider.GetRequiredService<AudienceProjectionStore>());
            services.AddScoped<IRelationshipBootstrapProcessor, RelationshipBootstrapProcessor>();
            services.AddScoped<FeedActivityProjectionStore>();
            services.AddScoped<IFeedActivityProjectionWriter>(provider =>
                provider.GetRequiredService<FeedActivityProjectionStore>());
            services.AddScoped<IFeedModerationDecisionRepository, FeedModerationDecisionRepository>();
            services.AddScoped<IEngagementSummaryProjectionWriter, EngagementSummaryProjectionStore>();
            services.AddScoped<IFeedFanoutProcessor, FeedFanoutProcessor>();
            services.AddScoped<IFeedRevocationProcessor, FeedRevocationProcessor>();
            services.AddScoped<IFeedMaintenanceProcessor, FeedMaintenanceProcessor>();
            services.AddScoped<IFeedReader, FeedReader>();
            services.AddHealthChecks().AddCheck<AudienceProjectionHealthCheck>(
                "feed-audience-projection",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: System.TimeSpan.FromSeconds(5));
            services.AddHealthChecks().AddCheck<FeedUserErasureHealthCheck>(
                "feed-user-erasure",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
                tags: ["ready", "privacy"],
                timeout: System.TimeSpan.FromSeconds(5));

            return services;
        }
    }
}
