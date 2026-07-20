using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace MySqlMigrations;

/// <summary>
/// Public, side-effecting helpers for applying raw-SQL MySQL migrations.
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies migrations declared by the supplied <see cref="IMigrationsConfiguration"/>
    /// types. Pass an empty/null sequence to no-op.
    /// </summary>
    public static async Task<IApplicationBuilder> ApplyMySqlMigrationsForAsync(
        this IApplicationBuilder app,
        IEnumerable<Type> migrationConfigurationTypes,
        CancellationToken cancellationToken)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));
        if (migrationConfigurationTypes == null)
            return app;

        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var loggerFactory = app.ApplicationServices.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        foreach (var t in migrationConfigurationTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyMySqlMigrationsCoreAsync(
                t,
                configuration,
                loggerFactory,
                cancellationToken);
        }

        return app;
    }

    /// <summary>
    /// Returns every concrete type in <paramref name="assemblies"/> that implements
    /// <see cref="IMigrationsConfiguration"/>.
    /// </summary>
    public static IEnumerable<Type> DiscoverMigrationConfigurations(IEnumerable<Assembly> assemblies)
    {
        if (assemblies == null)
            yield break;

        foreach (var asm in assemblies)
        {
            if (asm == null)
                continue;
            Type[] types;
            try
            { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null)
                    continue;
                if (t.IsAbstract || t.IsInterface)
                    continue;
                if (!typeof(IMigrationsConfiguration).IsAssignableFrom(t))
                    continue;
                yield return t;
            }
        }
    }

    private static async Task ApplyMySqlMigrationsCoreAsync(
        Type migrationConfigType,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (migrationConfigType == null)
            throw new ArgumentNullException(nameof(migrationConfigType));
        if (!typeof(IMigrationsConfiguration).IsAssignableFrom(migrationConfigType))
            throw new ArgumentException($"{migrationConfigType.FullName} does not implement IMigrationsConfiguration.", nameof(migrationConfigType));

        var connectionStringName = ReadConst(migrationConfigType, "ConnectionStringName")
            ?? throw new InvalidOperationException(
                $"{migrationConfigType.FullName} must expose `public const string ConnectionStringName`.");
        var migrationsPath = ReadConst(migrationConfigType, "MigrationsPath")
            ?? throw new InvalidOperationException(
                $"{migrationConfigType.FullName} must expose `public const string MigrationsPath`.");

        var connectionString = configuration[connectionStringName]
            ?? throw new InvalidOperationException(
                $"Connection string `{connectionStringName}` (declared by {migrationConfigType.Name}) was not found in the configuration pipeline.");

        var dbName = new MySqlConnectionStringBuilder(connectionString).Database;
        var logger = loggerFactory.CreateLogger<DatabaseMigrationService>();

        var service = new DatabaseMigrationService(connectionString, migrationsPath, dbName, logger);
        await service.MigrateAsync(cancellationToken);
    }

    private static string? ReadConst(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (field == null)
            return null;
        return field.GetRawConstantValue() as string ?? field.GetValue(null) as string;
    }
}
