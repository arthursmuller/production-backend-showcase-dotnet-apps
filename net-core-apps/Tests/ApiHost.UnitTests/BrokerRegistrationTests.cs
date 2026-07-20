using InspacyApiHost;
using MassTransit;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class BrokerRegistrationTests
{
    [Fact]
    public void Broker_disabled_registers_neither_transport_client_nor_consumers()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: false);
        var options = CreateOptions();
        options.WithBroker = false;

        builder.Api(options);

        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IIntegrationMessagePublisher));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ImplementationType == typeof(BrokerRegistrationProbeConsumer));
    }

    [Fact]
    public void Api_role_registers_transport_client_without_receive_consumers()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: true);
        var options = CreateOptions();

        builder.Api(options);

        Assert.True(options.WithBroker);
        Assert.False(options.WithConsumers);
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IIntegrationMessagePublisher));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ImplementationType == typeof(BrokerRegistrationProbeConsumer));
    }

    [Fact]
    public async Task Role_without_an_outbox_context_registers_no_outbox_readiness_check()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: true);
        builder.Configuration["Broker:OutboxTelemetry:Enabled"] = "false";

        builder.Api(CreateOptions());
        await using var provider = builder.Services.BuildServiceProvider();
        Assert.False(provider
            .GetRequiredService<IOptions<MassTransitBrokerOptions>>()
            .Value
            .OutboxTelemetry
            .Enabled);
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.DoesNotContain(
            registrations,
            registration => registration.Name.StartsWith(
                "masstransit-outbox-",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IMassTransitOutboxBacklogSampler<>)
                    || definition == typeof(IMassTransitOutboxBacklogState<>)
                    || definition == typeof(MassTransitOutboxBacklogMonitor<>)
                    || definition == typeof(MassTransitOutboxBacklogHealthCheck<>)));
    }

    [Fact]
    public void Consumer_worker_registers_transport_client_and_receive_consumer()
    {
        var builder = CreateBuilder(ApiRuntimeRole.ConsumerWorker, includeBrokerConfiguration: true);
        var options = CreateOptions();

        builder.Api(options);

        Assert.True(options.WithBroker);
        Assert.True(options.WithConsumers);
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IIntegrationMessagePublisher));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ImplementationType == typeof(BrokerRegistrationProbeConsumer));
    }

    [Fact]
    public async Task Migration_role_resolves_provider_dependencies_with_a_fail_closed_publisher()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Migration, includeBrokerConfiguration: true);
        var options = CreateOptions();

        builder.Api(options);

        Assert.False(options.WithBroker);
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IIntegrationMessagePublisher)
                && descriptor.ImplementationType == typeof(MigrationRoleMessagePublisher));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ImplementationType == typeof(MassTransitMessagePublisher));

        await using var provider = builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationMessagePublisher>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(
                new BrokerRegistrationProbeMessage(Guid.NewGuid()),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Contains("Migration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_role_rejects_an_explicit_broker_override()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Migration, includeBrokerConfiguration: true);
        var options = CreateOptions();
        options.WithBroker = true;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Api(options));

        Assert.Contains("Migration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broker_options_reject_concurrency_that_exceeds_prefetch()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: true);
        builder.Configuration["Broker:RabbitMq:PrefetchCount"] = "8";
        builder.Configuration["Broker:RabbitMq:ConsumerConcurrency"] = "9";

        builder.Api(CreateOptions());
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MassTransitBrokerOptions>>().Value);
    }

    [Fact]
    public void Broker_options_require_a_certificate_server_name_when_tls_is_enabled()
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: true);
        builder.Configuration["Broker:RabbitMq:Tls:Enabled"] = "true";
        builder.Configuration["Broker:RabbitMq:Tls:ServerName"] = string.Empty;

        builder.Api(CreateOptions());
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MassTransitBrokerOptions>>().Value);
    }

    [Theory]
    [InlineData("Broker:OutboxTelemetry:IntervalSeconds", "4")]
    [InlineData("Broker:OutboxTelemetry:IntervalSeconds", "121")]
    [InlineData("Broker:OutboxTelemetry:IntervalSeconds", "301")]
    [InlineData("Broker:OutboxTelemetry:QueryTimeoutSeconds", "0")]
    [InlineData("Broker:OutboxTelemetry:QueryTimeoutSeconds", "31")]
    [InlineData("Broker:OutboxTelemetry:MaximumPendingMessages", "0")]
    [InlineData("Broker:OutboxTelemetry:MaximumPendingMessages", "1000001")]
    [InlineData("Broker:OutboxTelemetry:MaximumOldestMessageAgeSeconds", "29")]
    [InlineData("Broker:OutboxTelemetry:MaximumOldestMessageAgeSeconds", "301")]
    [InlineData("Broker:OutboxTelemetry:MaximumSampleAgeSeconds", "14")]
    [InlineData("Broker:OutboxTelemetry:MaximumSampleAgeSeconds", "301")]
    [InlineData("Broker:OutboxTelemetry:MaximumSampleAgeSeconds", "34")]
    public void Broker_options_reject_unbounded_outbox_sampler_settings(string key, string value)
    {
        var builder = CreateBuilder(ApiRuntimeRole.Api, includeBrokerConfiguration: true);
        builder.Configuration[key] = value;

        builder.Api(CreateOptions());
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MassTransitBrokerOptions>>().Value);
    }

    private static WebApplicationBuilder CreateBuilder(
        ApiRuntimeRole role,
        bool includeBrokerConfiguration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BrokerRegistrationTests).Assembly.GetName().Name,
            EnvironmentName = "Production",
        });

        var settings = new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role.ToString(),
            ["Identity:ServiceUrl"] = "https://identity.test/",
            ["Identity:ClientId"] = "test_api",
            ["Identity:ClientSecret"] = "not-a-production-secret",
            ["Identity:ValidationMode"] = "Introspection",
        };

        if (includeBrokerConfiguration)
        {
            settings["Broker:Provider"] = "MassTransit";
            settings["Broker:Transport"] = "RabbitMq";
            settings["Broker:RabbitMq:Host"] = "127.0.0.1";
            settings["Broker:RabbitMq:Port"] = "5672";
            settings["Broker:RabbitMq:Username"] = "test";
            settings["Broker:RabbitMq:Password"] = "test";
            settings["Broker:RabbitMq:VirtualHost"] = "test";
            settings["Broker:RabbitMq:Tls:Enabled"] = "false";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static ApiOptions CreateOptions() => new()
    {
        AppName = "TestHost",
        EntryAssembly = typeof(BrokerRegistrationTests).Assembly,
        AuthPolicies = [],
        AllowedCorsDomains = ["https://client.test"],
        WithApplication = false,
        WithInfra = false,
        WithCqrs = new CqrsOptions { Enabled = false },
        WithControllers = false,
        WithRouteGroups = false,
    };
}

public sealed record BrokerRegistrationProbeMessage(Guid Id);

public sealed class BrokerRegistrationProbeConsumer : IConsumer<BrokerRegistrationProbeMessage>
{
    public Task Consume(ConsumeContext<BrokerRegistrationProbeMessage> context) =>
        Task.CompletedTask;
}
