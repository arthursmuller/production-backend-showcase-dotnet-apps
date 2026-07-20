using Following.Application.Providers.Categories;
using Following.Application.Providers.Notifications;
using Following.Application.Features.Followings;
using Following.Application.Providers.Privacy;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Following.Application
{
    public static class ApplicationExtensions
    {
        public static IServiceCollection ConfigureApplication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddScoped<CategoriesProvider>();
            services.AddScoped<NotificationProvider>();
            var cursorOptions = services.AddOptions<FollowingCursorOptions>()
                .Bind(configuration.GetSection(FollowingCursorOptions.SectionName))
                .Validate(
                    FollowingCursorOptions.IsValid,
                    "FollowingCursor requires a base64-encoded key of at least 32 bytes " +
                    "and a lifetime from 300 to 604800 seconds.");
            if (RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
                cursorOptions.ValidateOnStart();
            services.AddPlatformTimeProvider();
            services.AddSingleton<FollowingCursorCodec>();
            services.AddOptions<FollowingUserErasureOptions>()
                .Bind(configuration.GetSection(FollowingUserErasureOptions.SectionName))
                .Validate(
                    FollowingUserErasureOptions.IsValid,
                    "FollowingUserErasure requires bounded pages, cycles, leases, delays, and backlog age.")
                .ValidateOnStart();
            return services;
        }
    }
}
