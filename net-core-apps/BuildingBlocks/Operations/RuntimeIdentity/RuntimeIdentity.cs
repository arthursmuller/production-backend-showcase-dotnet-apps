using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InspacyApiHost;

/// <summary>
/// The independently deployable workload identities supported by every service.
/// The established API-prefixed name is retained for source compatibility.
/// </summary>
public enum ApiRuntimeRole
{
    Api,
    ConsumerWorker,
    JobWorker,
    Migration,
    Combined,
}

public static class RuntimeIdentity
{
    public const string RoleConfigurationKey = "Runtime:Role";

    public static ApiRuntimeRole Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Parse(configuration[RoleConfigurationKey]);
    }

    public static ApiRuntimeRole Parse(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return ApiRuntimeRole.Api;

        configured = configured.Trim();
        return Enum.TryParse<ApiRuntimeRole>(configured, ignoreCase: true, out var role)
            ? role
            : throw new InvalidOperationException(
                $"Unsupported Runtime:Role '{configured}'. Expected Api, ConsumerWorker, " +
                "JobWorker, Migration, or Combined.");
    }

    public static void ValidateEnvironment(ApiRuntimeRole role, string? environmentName)
    {
        if (role == ApiRuntimeRole.Combined
            && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Runtime:Role=Combined is restricted to Development. Deploy Api, ConsumerWorker, " +
                "JobWorker, and Migration roles independently.");
        }
    }

    public static bool ExposesBusinessRoutes(this ApiRuntimeRole role) =>
        role is ApiRuntimeRole.Api or ApiRuntimeRole.Combined;

    public static bool RunsConsumers(this ApiRuntimeRole role) =>
        role is ApiRuntimeRole.ConsumerWorker or ApiRuntimeRole.Combined;

    public static bool RunsJobs(this ApiRuntimeRole role) =>
        role is ApiRuntimeRole.JobWorker or ApiRuntimeRole.Combined;

    public static bool RunsMigrations(this ApiRuntimeRole role) =>
        role == ApiRuntimeRole.Migration;

    public static IServiceCollection AddPlatformTimeProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
