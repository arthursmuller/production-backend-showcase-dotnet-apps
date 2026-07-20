using System.Reflection;
using System.Security.Authentication;
using MassTransit;
using InspacyApiHost;
using MessageBrokerCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MessageBrokerExtensions;

public static class MassTransitBrokerExtensions
{
    public static IServiceCollection AddInspacyMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly consumerAssembly,
        bool registerConsumers,
        IReadOnlyCollection<Type>? outboxDbContextTypes = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(consumerAssembly);

        var outboxContexts = (outboxDbContextTypes ?? Array.Empty<Type>())
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(DbContext).IsAssignableFrom(type))
            .Distinct()
            .ToArray();

        foreach (var contextType in outboxContexts)
            AddOutboxBacklogMonitor(services, contextType);

        services.AddPlatformTimeProvider();

        services
            .AddOptions<MassTransitBrokerOptions>()
            .Bind(configuration.GetSection(MassTransitBrokerOptions.SectionName))
            .Validate(Validate, "Broker configuration is invalid.")
            .Validate(
                options => outboxContexts.Length == 0 || options.OutboxTelemetry.Enabled,
                "Outbox telemetry/readiness cannot be disabled when an outbox context is registered.")
            .ValidateOnStart();

        services.AddScoped<IIntegrationMessagePublisher, MassTransitMessagePublisher>();
        services.TryAddScoped(typeof(ConsumeLoggingScopeFilter<>));

        services.AddMassTransit(registration =>
        {
            foreach (var contextType in outboxContexts)
                AddEntityFrameworkOutbox(registration, contextType);

            if (registerConsumers)
                registration.AddConsumers(consumerAssembly);

            registration.AddDelayedMessageScheduler();
            registration.UsingRabbitMq((context, bus) =>
            {
                var options = context.GetRequiredService<IOptions<MassTransitBrokerOptions>>().Value;
                var rabbit = options.RabbitMq;

                bus.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                    host.Heartbeat(TimeSpan.FromSeconds(rabbit.HeartbeatSeconds));
                    if (rabbit.Tls.Enabled)
                    {
                        host.UseSsl(ssl =>
                        {
                            ssl.Protocol = SslProtocols.Tls12 | SslProtocols.Tls13;
                            ssl.ServerName = rabbit.Tls.ServerName;
                        });
                    }
                });

                bus.PrefetchCount = rabbit.PrefetchCount;
                bus.ConcurrentMessageLimit = rabbit.ConsumerConcurrency;
                bus.UseDelayedMessageScheduler();
                bus.UseInstrumentation(
                    serviceName: consumerAssembly.GetName().Name ?? "Inspacy");

                if (registerConsumers)
                    bus.UseConsumeFilter(typeof(ConsumeLoggingScopeFilter<>), context);

                if (registerConsumers)
                    bus.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static void AddEntityFrameworkOutbox(
        IBusRegistrationConfigurator registration,
        Type contextType)
    {
        var method = typeof(MassTransitBrokerExtensions)
            .GetMethod(
                nameof(AddEntityFrameworkOutboxCore),
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AddEntityFrameworkOutboxCore));

        method.MakeGenericMethod(contextType).Invoke(null, [registration]);
    }

    private static void AddOutboxBacklogMonitor(
        IServiceCollection services,
        Type contextType)
    {
        var method = typeof(MassTransitBrokerExtensions)
            .GetMethod(
                nameof(AddOutboxBacklogMonitorCore),
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AddOutboxBacklogMonitorCore));

        method.MakeGenericMethod(contextType).Invoke(null, [services]);
    }

    private static void AddOutboxBacklogMonitorCore<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        services.AddSingleton<IMassTransitOutboxBacklogSampler<TContext>,
            MassTransitOutboxBacklogSampler<TContext>>();
        services.AddSingleton<MassTransitOutboxBacklogMonitor<TContext>>();
        services.AddSingleton<IMassTransitOutboxBacklogState<TContext>>(provider =>
            provider.GetRequiredService<MassTransitOutboxBacklogMonitor<TContext>>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<MassTransitOutboxBacklogMonitor<TContext>>());
        services.AddSingleton<MassTransitOutboxBacklogHealthCheck<TContext>>();
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            $"masstransit-outbox-{typeof(TContext).Name}",
            provider => provider.GetRequiredService<MassTransitOutboxBacklogHealthCheck<TContext>>(),
            HealthStatus.Unhealthy,
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(2)));
    }

    private static void AddEntityFrameworkOutboxCore<TContext>(
        IBusRegistrationConfigurator registration)
        where TContext : DbContext
    {
        registration.AddEntityFrameworkOutbox<TContext>(outbox =>
        {
            outbox.UseMySql();
            outbox.UseBusOutbox();
            outbox.QueryDelay = TimeSpan.FromSeconds(1);
            outbox.DuplicateDetectionWindow = TimeSpan.FromHours(24);
        });
    }

    private static bool Validate(MassTransitBrokerOptions options)
    {
        if (!string.Equals(options.Provider, "MassTransit", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(options.Transport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(options.RabbitMq.Host)
            || string.IsNullOrWhiteSpace(options.RabbitMq.Username)
            || string.IsNullOrWhiteSpace(options.RabbitMq.Password)
            || string.IsNullOrWhiteSpace(options.RabbitMq.VirtualHost))
        {
            return false;
        }

        return options.RabbitMq.Port > 0
            && options.RabbitMq.PrefetchCount is > 0 and <= 1024
            && options.RabbitMq.ConsumerConcurrency is > 0 and <= 256
            && options.RabbitMq.ConsumerConcurrency <= options.RabbitMq.PrefetchCount
            && options.OutboxTelemetry.IntervalSeconds is >= 5 and <= 120
            && options.OutboxTelemetry.QueryTimeoutSeconds is >= 1 and <= 30
            && options.OutboxTelemetry.MaximumPendingMessages is >= 1 and <= 1_000_000
            && options.OutboxTelemetry.MaximumOldestMessageAgeSeconds is >= 30 and <= 300
            && options.OutboxTelemetry.MaximumSampleAgeSeconds is >= 15 and <= 300
            && options.OutboxTelemetry.MaximumSampleAgeSeconds
                >= (2 * options.OutboxTelemetry.IntervalSeconds)
                    + options.OutboxTelemetry.QueryTimeoutSeconds
            && options.OutboxTelemetry.MaximumSampleAgeSeconds
                <= options.OutboxTelemetry.MaximumOldestMessageAgeSeconds
            && (!options.RabbitMq.Tls.Enabled
                || !string.IsNullOrWhiteSpace(options.RabbitMq.Tls.ServerName))
            && options.RabbitMq.HeartbeatSeconds is >= 5 and <= 300;
    }
}
