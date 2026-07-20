using MassTransit;
using Moderation.Infra.Database.Ef;

namespace Moderation.API.AppInterfaces.Queues;

public abstract class ModerationConsumerDefinition<TConsumer> : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    protected ModerationConsumerDefinition(string endpointName)
    {
        EndpointName = endpointName;
        ConcurrentMessageLimit = 16;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 32;
        endpointConfigurator.UseMessageRetry(retry =>
            retry.Intervals(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
        endpointConfigurator.UseDelayedRedelivery(redelivery =>
            redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        endpointConfigurator.UseEntityFrameworkOutbox<ModerationDbContext>(context);
    }
}
