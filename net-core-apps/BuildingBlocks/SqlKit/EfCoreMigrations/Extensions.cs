using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfCoreMigrations;

/// <summary>
/// Generic, cancellation-aware schema helpers for EF Core <see cref="DbContext"/> derivatives.
/// </summary>
public static class EfCoreMigrationsExtensions
{
    /// <summary>
    /// Applies migrations for every supplied context type in sequence. An empty sequence is a no-op.
    /// </summary>
    public static async Task<IApplicationBuilder> ApplySchemaForAllAsync(
        this IApplicationBuilder app,
        IEnumerable<Type> contextTypes,
        CancellationToken cancellationToken)
    {
        if (contextTypes is null)
            return app;

        using var scope = app.ApplicationServices.CreateScope();
        foreach (var contextType in contextTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplySchemaCoreAsync(scope.ServiceProvider, contextType, cancellationToken);
        }

        return app;
    }

    /// <summary>
    /// Scans the supplied assemblies for concrete <see cref="DbContext"/> derivatives.
    /// </summary>
    public static IEnumerable<Type> DiscoverContexts(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null)
            yield break;

        foreach (var assembly in assemblies)
        {
            if (assembly is null)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).ToArray()!;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;
                if (typeof(DbContext).IsAssignableFrom(type))
                    yield return type;
            }
        }
    }

    private static Task ApplySchemaCoreAsync(
        IServiceProvider services,
        Type contextType,
        CancellationToken cancellationToken)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger($"EfCoreMigrations.{contextType.Name}");
        var context = (DbContext)services.GetRequiredService(contextType);
        var underK8s = string.Equals(
            services.GetRequiredService<IConfiguration>()["OrchestratorType"],
            "K8S",
            StringComparison.OrdinalIgnoreCase);

        return ExecuteMigrationWithRetryAsync(
            token => context.Database.MigrateAsync(token),
            underK8s,
            logger,
            contextType.Name,
            cancellationToken);
    }

    internal static async Task ExecuteMigrationWithRetryAsync(
        Func<CancellationToken, Task> migrateAsync,
        bool underK8s,
        ILogger logger,
        string contextName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrateAsync);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextName);

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogInformation(
                    "Applying migrations for {DbContext} (attempt {Attempt}/{Max})",
                    contextName,
                    attempt,
                    maxAttempts);

                await migrateAsync(cancellationToken);
                logger.LogInformation("Migrations applied for {DbContext}", contextName);
                return;
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                && attempt < maxAttempts
                && !underK8s
                && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(
                    exception,
                    "Migration attempt {Attempt} for {DbContext} failed. Retrying in {Delay}s.",
                    attempt,
                    contextName,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
