using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Following.Application
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
