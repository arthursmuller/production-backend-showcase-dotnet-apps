using Comment.Application.Features.Comments.Queries;
using Comment.Application.Features.Interactions;
using Comment.Application.Features.Media.Commands;
using Comment.Application.Options;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Comment.Application.Features.Privacy;
using Comment.Application.Features.Moderation.Commands;

namespace Comment.Application
{
    public static class ApplicationExtensions
    {
        public static IServiceCollection ConfigureApplication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
            services.AddScoped<CommentMediaIngestionService>();
            services.AddScoped<ScheduleCommentUserErasureCommand>();
            services.AddScoped<ApplyCommentModerationDecisionCommand>();
            services.AddScoped<CommentMediaReconciliationCycle>();
            var cursorOptions = services.AddOptions<CommentCursorOptions>()
                .Bind(configuration.GetSection(CommentCursorOptions.SectionName))
                .Validate(
                    CommentCursorOptions.IsValid,
                    "CommentCursor requires a base64-encoded key of at least 32 bytes and a lifetime from 300 to 604800 seconds.");
            if (RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
                cursorOptions.ValidateOnStart();
            services.AddPlatformTimeProvider();
            var mediaOptions = configuration
                .GetSection(CommentMediaIngestionOptions.SectionName)
                .Get<CommentMediaIngestionOptions>()
                ?? new CommentMediaIngestionOptions();
            if (!mediaOptions.IsValid())
                throw new InvalidOperationException(
                    $"{CommentMediaIngestionOptions.SectionName} configuration is invalid.");
            services.AddSingleton(mediaOptions);
            services.AddSingleton<CommentCursorCodec>();

            services.AddScoped<CommentInteractionReadProjector>();
            return services;
        }
    }
}
