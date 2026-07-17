using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comment.Application
{
    public static class ApplicationExtensions
    {
        public static IServiceCollection ConfigureApplication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services;
        }
    }
}
