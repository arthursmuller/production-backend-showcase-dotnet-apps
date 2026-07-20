using Microsoft.EntityFrameworkCore;

namespace Inspacy.Data.MariaDb;

/// <summary>
/// Applies the company-owned MariaDB provider policy to EF Core contexts.
/// </summary>
public static class MariaDbDbContextOptionsExtensions
{
    private const int MaximumRetryCount = 3;
    private const int DesignTimeCommandTimeoutSeconds = 30;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly MariaDbServerVersion ServerVersion =
        new(new Version(10, 6, 27));

    /// <summary>
    /// Configures a runtime context with the shared server, resilience, timeout, and
    /// command-telemetry policy.
    /// </summary>
    public static DbContextOptionsBuilder UseMariaDbRuntime(
        this DbContextOptionsBuilder options,
        MariaDbRuntimeConnection connection,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        ConfigureProvider(
            options,
            connection.ConnectionString,
            connection.CommandTimeoutSeconds);
        options.AddMariaDbCommandTelemetry(serviceProvider);
        return options;
    }

    internal static DbContextOptionsBuilder ConfigureProvider(
        DbContextOptionsBuilder options,
        string connectionString,
        int commandTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A MariaDB connection string is required.",
                nameof(connectionString));
        }
        if (commandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutSeconds),
                "The MariaDB command timeout must be positive.");
        }

        options.UseMySql(
            connectionString,
            ServerVersion,
            mysql =>
            {
                mysql.CommandTimeout(commandTimeoutSeconds);
                mysql.EnableRetryOnFailure(
                    MaximumRetryCount,
                    MaximumRetryDelay,
                    errorNumbersToAdd: null);
            });
        return options;
    }

    internal static int DefaultDesignTimeCommandTimeoutSeconds =>
        DesignTimeCommandTimeoutSeconds;
}

/// <summary>
/// Creates EF Core options exclusively for design-time tooling, which runs outside
/// the secret-aware application host.
/// </summary>
public static class MariaDbDesignTimeOptions
{
    public const string ConnectionStringEnvironmentVariable =
        "Database__MariaDbSqlConnection";

    public static DbContextOptions<TContext> Create<TContext>()
        where TContext : DbContext =>
        Create<TContext>(Environment.GetEnvironmentVariable);

    internal static DbContextOptions<TContext> Create<TContext>(
        Func<string, string?> readEnvironmentVariable)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        var connectionString = readEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)
            || connectionString.Contains("{{secret:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} must resolve to a non-placeholder " +
                "secret when running EF Core design-time commands.");
        }

        var builder = new DbContextOptionsBuilder<TContext>();
        MariaDbDbContextOptionsExtensions.ConfigureProvider(
            builder,
            connectionString,
            MariaDbDbContextOptionsExtensions.DefaultDesignTimeCommandTimeoutSeconds);
        return builder.Options;
    }
}
