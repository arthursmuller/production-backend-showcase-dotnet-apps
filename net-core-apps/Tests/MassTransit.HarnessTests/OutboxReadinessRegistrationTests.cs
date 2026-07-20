using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace MassTransit.HarnessTests;

public sealed class OutboxReadinessRegistrationTests
{
    [Fact]
    public async Task Outbox_context_registers_a_ready_only_memory_health_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInspacyMassTransit(
            CreateConfiguration(outboxReadinessEnabled: true),
            typeof(OutboxReadinessRegistrationTests).Assembly,
            registerConsumers: false,
            [typeof(OutboxRegistrationProbeDbContext)]);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType
                == typeof(IMassTransitOutboxBacklogSampler<OutboxRegistrationProbeDbContext>));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType
                == typeof(MassTransitOutboxBacklogMonitor<OutboxRegistrationProbeDbContext>));

        await using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var registration = Assert.Single(
            registrations,
            item => item.Name == "masstransit-outbox-OutboxRegistrationProbeDbContext");

        Assert.Contains("ready", registration.Tags);
        Assert.DoesNotContain("live", registration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal(TimeSpan.FromSeconds(2), registration.Timeout);
    }

    [Fact]
    public async Task Outbox_context_rejects_disabling_the_readiness_gate_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInspacyMassTransit(
            CreateConfiguration(outboxReadinessEnabled: false),
            typeof(OutboxReadinessRegistrationTests).Assembly,
            registerConsumers: false,
            [typeof(OutboxRegistrationProbeDbContext)]);
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("cannot be disabled", error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(bool outboxReadinessEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Broker:Provider"] = "MassTransit",
                ["Broker:Transport"] = "RabbitMq",
                ["Broker:RabbitMq:Host"] = "127.0.0.1",
                ["Broker:RabbitMq:Username"] = "test",
                ["Broker:RabbitMq:Password"] = "test",
                ["Broker:RabbitMq:VirtualHost"] = "test",
                ["Broker:RabbitMq:Tls:Enabled"] = "false",
                ["Broker:OutboxTelemetry:Enabled"] = outboxReadinessEnabled.ToString(),
            })
            .Build();

    private sealed class OutboxRegistrationProbeDbContext : DbContext
    {
    }
}
