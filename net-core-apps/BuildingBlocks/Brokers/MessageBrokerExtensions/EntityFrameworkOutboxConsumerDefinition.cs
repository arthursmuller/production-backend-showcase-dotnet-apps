using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace MessageBrokerExtensions;

/// <summary>
/// Configures a durable typed MassTransit endpoint with the service-owned EF inbox/outbox.
/// Message bindings are supplied exclusively by MassTransit's contract publish topology.
/// </summary>
public abstract class EntityFrameworkOutboxConsumerDefinition<TConsumer, TContext>
    : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
    where TContext : DbContext
{
    protected EntityFrameworkOutboxConsumerDefinition(
        string endpointName,
        ushort prefetchCount = 32,
        int concurrencyLimit = 16)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
            throw new ArgumentException("A durable endpoint name is required.", nameof(endpointName));
        if (prefetchCount is 0 or > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prefetchCount),
                "Prefetch must be between 1 and 1024.");
        }
        if (concurrencyLimit is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concurrencyLimit),
                "Consumer concurrency must be between 1 and 256.");
        }

        EndpointName = endpointName;
        ConcurrentMessageLimit = concurrencyLimit;
        PrefetchCount = prefetchCount;
    }

    private ushort PrefetchCount { get; }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = PrefetchCount;
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1)));
        endpointConfigurator.UseDelayedRedelivery(redelivery => redelivery.Intervals(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30)));
        endpointConfigurator.UseEntityFrameworkOutbox<TContext>(context);
    }
}
