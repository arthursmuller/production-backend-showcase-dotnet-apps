using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EfCoreMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InspacyApiHost;

internal static class ReadinessHealthChecks
{
    public static void AddDatabaseChecks(
        IServiceCollection services,
        IEnumerable<Assembly> assemblies)
    {
        var contextTypes = EfCoreMigrationsExtensions.DiscoverContexts(assemblies)
            .Distinct()
            .ToArray();
        if (contextTypes.Length == 0)
            return;

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "database",
            serviceProvider => new DbContextReadinessCheck(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                contextTypes),
            HealthStatus.Unhealthy,
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(5)));
    }
}

internal sealed class DbContextReadinessCheck(
    IServiceScopeFactory scopeFactory,
    IReadOnlyCollection<Type> contextTypes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var contextType in contextTypes)
        {
            if (scope.ServiceProvider.GetService(contextType) is not DbContext databaseContext)
            {
                return HealthCheckResult.Unhealthy(
                    $"Database context '{contextType.Name}' is not registered for this role.");
            }

            try
            {
                if (!await databaseContext.Database.CanConnectAsync(cancellationToken))
                {
                    return HealthCheckResult.Unhealthy(
                        $"Database for context '{contextType.Name}' is unavailable.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy(
                    $"Database check failed for context '{contextType.Name}'.",
                    exception);
            }
        }

        return HealthCheckResult.Healthy();
    }
}
