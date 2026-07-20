using Microsoft.Extensions.Configuration;
using MySqlConnector;
using InspacyApiHost;

namespace Inspacy.Data.MariaDb;

public sealed record MariaDbRuntimeConnection(
    string ConnectionString,
    string RuntimeRole,
    int MaximumPoolSize,
    int CommandTimeoutSeconds);

/// <summary>
/// Applies the process-role connection budget to a resolved MariaDB secret.
/// The normalized connection string is sensitive and must never be logged.
/// </summary>
public static class MariaDbConnectionPolicy
{
    private const int AbsoluteMaximumPoolSize = 64;

    public static MariaDbRuntimeConnection Create(
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(serviceName)
            || serviceName.Length > 64
            || serviceName.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "A bounded alphanumeric service name is required.",
                nameof(serviceName));
        }

        var rawConnectionString = configuration["Database:MariaDbSqlConnection"];
        if (string.IsNullOrWhiteSpace(rawConnectionString)
            || rawConnectionString.Contains("{{secret:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Database:MariaDbSqlConnection must resolve to a non-placeholder secret.");
        }

        var runtimeRole = RuntimeIdentity.Resolve(configuration).ToString();
        var poolKey = $"Database:PoolSizes:{runtimeRole}";
        var maximumPoolSize = ReadBoundedInteger(
            configuration,
            poolKey,
            minimum: 1,
            maximum: AbsoluteMaximumPoolSize);
        var connectionTimeout = ReadBoundedInteger(
            configuration,
            "Database:ConnectionTimeoutSeconds",
            defaultValue: 5,
            minimum: 1,
            maximum: 15);
        var commandTimeout = ReadBoundedInteger(
            configuration,
            "Database:CommandTimeoutSeconds",
            defaultValue: 30,
            minimum: 1,
            maximum: 120);
        var connectionIdleTimeout = ReadBoundedInteger(
            configuration,
            "Database:ConnectionIdleTimeoutSeconds",
            defaultValue: 180,
            minimum: 30,
            maximum: 1_800);
        var connectionLifetime = ReadBoundedInteger(
            configuration,
            "Database:ConnectionLifetimeSeconds",
            defaultValue: 900,
            minimum: 60,
            maximum: 3_600);
        var requireTls = ReadBoolean(configuration, "Database:RequireTls", defaultValue: true);
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";
        if (!requireTls && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Database:RequireTls=false is permitted only in Development.");
        }

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(rawConnectionString);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(
                "Database:MariaDbSqlConnection is not a valid MariaDB connection string.",
                error);
        }

        builder.Pooling = true;
        builder.MinimumPoolSize = 0;
        builder.MaximumPoolSize = checked((uint)maximumPoolSize);
        builder.ConnectionTimeout = checked((uint)connectionTimeout);
        builder.DefaultCommandTimeout = checked((uint)commandTimeout);
        builder.ConnectionIdleTimeout = checked((uint)connectionIdleTimeout);
        builder.ConnectionLifeTime = checked((uint)connectionLifetime);
        builder.ConnectionReset = true;
        builder.PersistSecurityInfo = false;
        builder.AllowLoadLocalInfile = false;
        builder.ApplicationName = $"inspacy-{serviceName}/{runtimeRole}";
        if (requireTls)
            builder.SslMode = MySqlSslMode.VerifyFull;

        return new MariaDbRuntimeConnection(
            builder.ConnectionString,
            runtimeRole,
            maximumPoolSize,
            commandTimeout);
    }

    private static int ReadBoundedInteger(
        IConfiguration configuration,
        string key,
        int minimum,
        int maximum,
        int? defaultValue = null)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (defaultValue.HasValue)
                return defaultValue.Value;
            throw new InvalidOperationException($"{key} is required.");
        }

        if (!int.TryParse(configured, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{key} must be an integer from {minimum} through {maximum}.");
        }

        return value;
    }

    private static bool ReadBoolean(
        IConfiguration configuration,
        string key,
        bool defaultValue)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
            return defaultValue;
        if (bool.TryParse(configured, out var value))
            return value;
        throw new InvalidOperationException($"{key} must be true or false.");
    }
}
