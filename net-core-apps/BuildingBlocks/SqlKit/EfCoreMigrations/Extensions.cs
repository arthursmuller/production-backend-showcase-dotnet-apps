using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfCoreMigrations;

/// <summary>
/// Generic schema-apply helpers for EF Core <see cref="DbContext"/> derivatives.
/// Replaces the per-service <c>ApplySchema</c> extension methods that used to
/// live next to each <c>DbContext</c>.
/// </summary>
public static class EfCoreMigrationsExtensions
{
    /// <summary>
    /// Resolves the supplied <paramref name="contextType"/> from the application
    /// service provider and runs <see cref="DatabaseFacade.Migrate"/> with retry
    /// (skipped when running under K8S so the pod can crash-loop into readiness).
    /// </summary>
    public static IApplicationBuilder ApplySchema(this IApplicationBuilder app, Type contextType)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        if (contextType == null) throw new ArgumentNullException(nameof(contextType));
        if (!typeof(DbContext).IsAssignableFrom(contextType))
            throw new ArgumentException($"{contextType.FullName} is not a DbContext.", nameof(contextType));

        using var scope = app.ApplicationServices.CreateScope();
        ApplySchemaCore(scope.ServiceProvider, contextType);
        return app;
    }

    /// <summary>
    /// Strongly-typed convenience wrapper around <see cref="ApplySchema(IApplicationBuilder, Type)"/>.
    /// </summary>
    public static IApplicationBuilder ApplySchema<TContext>(this IApplicationBuilder app)
        where TContext : DbContext
        => app.ApplySchema(typeof(TContext));

    /// <summary>
    /// Applies <see cref="DatabaseFacade.Migrate"/> for every context type in
    /// <paramref name="contextTypes"/>. When the sequence is empty the call is a
    /// no-op.
    /// </summary>
    public static IApplicationBuilder ApplySchemaForAll(this IApplicationBuilder app, IEnumerable<Type> contextTypes)
    {
        if (contextTypes == null) return app;
        using var scope = app.ApplicationServices.CreateScope();
        foreach (var t in contextTypes)
            ApplySchemaCore(scope.ServiceProvider, t);
        return app;
    }

    /// <summary>
    /// Scans the supplied assemblies for concrete <see cref="DbContext"/>
    /// derivatives. Used by the <c>WithDatabases</c> auto-discovery path.
    /// </summary>
    public static IEnumerable<Type> DiscoverContexts(IEnumerable<Assembly> assemblies)
    {
        if (assemblies == null) yield break;

        foreach (var asm in assemblies)
        {
            if (asm == null) continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!typeof(DbContext).IsAssignableFrom(t)) continue;
                yield return t;
            }
        }
    }

    private static void ApplySchemaCore(IServiceProvider services, Type contextType)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger($"EfCoreMigrations.{contextType.Name}");

        var ctx = (DbContext)services.GetRequiredService(contextType);

        const int maxRetries = 10;
        var underK8s = string.Equals(
            services.GetRequiredService<IConfiguration>()["OrchestratorType"],
            "K8S",
            StringComparison.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation(
                    "Applying migrations for {DbContext} (attempt {Attempt}/{Max})",
                    contextType.Name, attempt, maxRetries);

                ctx.Database.Migrate();
                logger.LogInformation("Migrations applied for {DbContext}", contextType.Name);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && !underK8s)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(ex,
                    "Migration attempt {Attempt} for {DbContext} failed: {Message}. Retrying in {Delay}s.",
                    attempt, contextType.Name, ex.Message, delay.TotalSeconds);
                Thread.Sleep(delay);
            }
        }

        // Final attempt – let exceptions bubble.
        ctx.Database.Migrate();
    }
}
