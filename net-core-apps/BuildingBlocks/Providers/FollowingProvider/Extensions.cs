using AuthenticationProvider;
using FollowingProvider.Abstractions;
using FollowingProvider.Providers.Api;
using HttpClientHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FollowingProvider;

public static class FollowingProviderExtensions
{
    public static IServiceCollection ConfigureFollowingProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        ClientCredentialTokenConfig clientTokenConfig)
    {
        services.ConfigureAuthenticationProvider(configuration);
        ConfigureApi(services, configuration);
        services.AddScoped<IFollowingService, FollowingService>();
        services.AddSingleton(clientTokenConfig);

        return services;
    }

    private static void ConfigureApi(IServiceCollection services, IConfiguration configuration)
    {
        var url = GetServiceUrl(configuration);

        if (!string.IsNullOrEmpty(url))
        {
            HttpProvider.New<IFollowingApi, FollowingApiProvider>(
                services,
                url,
                timeout: GetRequestTimeout(configuration));
        }
    }

    private static TimeSpan GetRequestTimeout(IConfiguration configuration)
    {
        const int defaultSeconds = 15;
        var configured = configuration?["Following:RequestTimeoutSeconds"];
        if (string.IsNullOrWhiteSpace(configured))
            return TimeSpan.FromSeconds(defaultSeconds);
        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds)
            || seconds is < 1 or > 120)
        {
            throw new InvalidOperationException(
                "Following:RequestTimeoutSeconds must be between 1 and 120.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static string? GetServiceUrl(IConfiguration configuration) =>
        configuration["Following:ServiceUrl"];
}
