using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace IdentityProvider
{
    public class IdentityService : IIdentityService
    {
        public AuthUser User { get; }

        public IdentityService(IHttpContextAccessor httpContextAccessor)
        {
            if (httpContextAccessor == null)
                throw new ArgumentNullException(nameof(httpContextAccessor));

            var claims = httpContextAccessor.HttpContext?.User?.Identities?.FirstOrDefault()?.Claims;
            int.TryParse(claims?.FirstOrDefault(c => c.Type == "sub")?.Value, out var userId);
            var name = claims?.FirstOrDefault(c => c.Type == "name")?.Value ?? string.Empty;

            User = new AuthUser(userId, name);
        }
    }

    public static class IdentityProviderExtensions
    {
        public static IServiceCollection ConfigureIdentityProvider(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IIdentityService, IdentityService>();
            return services;
        }
    }
}
