using Comment.Application.Features.Privacy;
using Comment.Application.Features.Visibility;
using Comment.Application.Providers.IntegrationEvents;
using Comment.Application.Providers.Interactions;
using Comment.Infra.Database;
using Comment.Infra.Persistence.Privacy;
using Comment.Infra.Persistence.Visibility;
using Comment.Infra.Providers;
using Comment.Infra.Providers.IntegrationEvents;
using Comment.Infra.Providers.Interactions;
using Comment.Infra.Providers.Privacy;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;

namespace Comment.Infra
{
    public static class InfraestructureExtensions
    {
        public static IServiceCollection ConfigureInfraestructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigureDb(configuration);
            services.ConfigureProviders(configuration);

            services.AddOptions<CommentUserErasureOptions>()
                .Bind(configuration.GetSection(CommentUserErasureOptions.SectionName))
                .Validate(CommentUserErasureOptions.IsValid,
                    "CommentUserErasure contains an unsafe or unbounded value.")
                .ValidateOnStart();
            services.AddSingleton<CommentUserErasureTelemetry>();
            services.AddScoped<ICommentPrivacyEventPublisher, CommentPrivacyEventPublisher>();
            var runtimeRole = RuntimeIdentity.Resolve(configuration);
            if (runtimeRole.RunsJobs())
                services.AddScoped<ICommentUserErasureProcessor, CommentUserErasureProcessor>();

            services.AddOptions<CommentAudienceProjectionOptions>()
                .Bind(configuration.GetSection(CommentAudienceProjectionOptions.SectionName))
                .Validate(CommentAudienceProjectionOptions.IsValid,
                    "CommentAudienceProjection contains an unsafe or unbounded value.")
                .ValidateOnStart();
            services.AddSingleton<CommentAudienceProjectionTelemetry>();
            services.AddScoped<CommentRelationshipProjectionStore>();
            services.AddScoped<ICommentRelationshipProjectionWriter>(provider =>
                provider.GetRequiredService<CommentRelationshipProjectionStore>());
            services.AddScoped<ICommentRelationshipProjectionReconciler>(provider =>
                provider.GetRequiredService<CommentRelationshipProjectionStore>());
            services.AddScoped<CommentAudienceSqlPolicy>();
            services.AddScoped<ICommentAudienceAuthorization>(provider =>
                provider.GetRequiredService<CommentAudienceSqlPolicy>());
            if (runtimeRole.RunsJobs())
                services.AddScoped<ICommentRelationshipBootstrapProcessor,
                    CommentRelationshipBootstrapProcessor>();
            services.AddHealthChecks().AddCheck<CommentAudienceProjectionHealthCheck>(
                "comment-audience-projection",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(5));

            services.AddScoped<ICommentInteractionEventPort, CommentInteractionEventAdapter>();
            services.AddScoped<ICommentIntegrationEventPublisher,
                MassTransitCommentIntegrationEventPublisher>();
            return services;
        }
    }
}
