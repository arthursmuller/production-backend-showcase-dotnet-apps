using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inspacy.Data.MariaDb;

public sealed class MariaDbCommandTelemetryOptions
{
    public const string ConfigurationKey = "Database:SlowQueryThresholdMilliseconds";
    public const int MinimumThresholdMilliseconds = 1;
    public const int MaximumThresholdMilliseconds = 60_000;

    public MariaDbCommandTelemetryOptions(int slowQueryThresholdMilliseconds)
    {
        if (slowQueryThresholdMilliseconds is < MinimumThresholdMilliseconds
            or > MaximumThresholdMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slowQueryThresholdMilliseconds),
                $"The slow-query threshold must be from {MinimumThresholdMilliseconds} through " +
                $"{MaximumThresholdMilliseconds} milliseconds.");
        }

        SlowQueryThreshold = TimeSpan.FromMilliseconds(slowQueryThresholdMilliseconds);
    }

    public TimeSpan SlowQueryThreshold { get; }

    public static MariaDbCommandTelemetryOptions Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[ConfigurationKey];
        if (!int.TryParse(configured, out var threshold)
            || threshold is < MinimumThresholdMilliseconds or > MaximumThresholdMilliseconds)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is required and must be an integer from " +
                $"{MinimumThresholdMilliseconds} through {MaximumThresholdMilliseconds}.");
        }

        return new MariaDbCommandTelemetryOptions(threshold);
    }
}

public static class MariaDbCommandTelemetryExtensions
{
    public static IServiceCollection AddMariaDbCommandTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = MariaDbCommandTelemetryOptions.Create(configuration);
        services.TryAddSingleton(options);
        services.TryAddSingleton<MariaDbCommandTelemetryInterceptor>();
        return services;
    }

    public static DbContextOptionsBuilder AddMariaDbCommandTelemetry(
        this DbContextOptionsBuilder options,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        options.AddInterceptors(
            serviceProvider.GetRequiredService<MariaDbCommandTelemetryInterceptor>());
        return options;
    }
}
