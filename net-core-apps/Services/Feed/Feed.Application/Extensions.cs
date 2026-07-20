using Feed.Application.Features.Moderation;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Feed.Application
{
    public static class ApplicationExtensions
    {
        public static IServiceCollection ConfigureApplication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddScoped<ApplyFeedModerationDecisionCommand>();
            services.AddPlatformTimeProvider();
            return services;
        }
    }
}
