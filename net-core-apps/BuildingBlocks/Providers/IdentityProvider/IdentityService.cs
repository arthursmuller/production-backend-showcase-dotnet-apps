using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace IdentityProvider
{
    public class IdentityService : IIdentityService
    {
        public AuthUser User { get; set; }
        public string ReqIpAddress { get; set; }
        public string AccessToken { get; set; }
        public string ReqTimeZone { get; set; }

        public IdentityService(IHttpContextAccessor httpContextAccessor)
        {
            if (httpContextAccessor == null) throw new ArgumentNullException(nameof(httpContextAccessor));

            ReqIpAddress = httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
            ReqTimeZone = httpContextAccessor?.HttpContext?.Request?.Headers["TimeZone"];
            AccessToken = httpContextAccessor?.HttpContext?.Request?.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer", "") ?? string.Empty;

            var claims = httpContextAccessor?.HttpContext?.User?.Identities?.FirstOrDefault()?.Claims;
            int.TryParse(claims?.FirstOrDefault(c => c.Type == "sub")?.Value, out var userId);
            var name = claims?.FirstOrDefault(c => c.Type == "name")?.Value ?? string.Empty;
            var cpf = claims?.FirstOrDefault(c => c.Type == "cpf")?.Value ?? string.Empty;
            var isVerified = httpContextAccessor?.HttpContext?.User?.IsInRole("VerifiedUser");
            var isAdmin = httpContextAccessor?.HttpContext?.User?.IsInRole("SysAdmin") ?? false;

            User = new AuthUser(userId, name, cpf, isVerified, isAdmin, string.Empty);
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
