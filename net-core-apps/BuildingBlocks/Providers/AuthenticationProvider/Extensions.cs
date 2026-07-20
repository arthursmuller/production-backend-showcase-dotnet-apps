using AuthenticationProvider.Abstractions;
using AuthenticationProvider.Providers.Api;
using HttpClientHelper;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace AuthenticationProvider;

public static class AuthenticationProviderExtensions
{
    public static IServiceCollection ConfigureAuthenticationProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformTimeProvider();
        services.TryAddSingleton<AccessTokenCache>();
        ConfigureApi(services, configuration);
        services.AddScoped<IAuthService, AuthenticationService>();

        return services;
    }

    private static void ConfigureApi(IServiceCollection services, IConfiguration configuration)
    {
        var url = GetServiceUrl(configuration);

        if (!string.IsNullOrEmpty(url))
        {
            // Client-credential token requests are POSTs but do not mutate
            // application state, so a transient retry is safe at this boundary.
            HttpProvider.New<IAuthApi, AuthenticationApiProvider>(
                services,
                url,
                retryUnsafeMethods: true);
        }
    }

    private static string? GetServiceUrl(IConfiguration configuration) =>
        configuration["Identity:ServiceUrl"];
}
