using CategoriesProvider;
using FeedProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Comment.Infra.Providers.Notifications;
using Comment.Infra.Providers.Storage;
using UserProvider;
using IdentityProvider;
using Posts.Provider;
using Comment.Application.Abstractions;

namespace Comment.Infra.Providers
{
    public static class ProvidersExtensions
    {
        public static IServiceCollection ConfigureProviders(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .ConfigureIdentityProvider()
                .ConfigureFeedProvider(configuration)
                .ConfigureUserProvider(configuration)
                .ConfigureCategoriesProvider(configuration)
                .ConfigureStorage(configuration)
                .ConfigurePostsProvider(configuration)
                .ConfigureNotifications(configuration);

            services.AddScoped<IIdentity, IdentityAdapter>();
            services.AddScoped<IFeed, FeedAdapter>();
            services.AddScoped<IPosts, PostsAdapter>();
            services.AddScoped<ICategories, CategoryAdapter>();
            services.AddScoped<IUserAnalytics, UserAnalyticsAdapter>();
            
            return services;
        }
    }
}
