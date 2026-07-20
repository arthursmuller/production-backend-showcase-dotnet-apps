using Feed.Application.Features.Feeds;
using Feed.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Feed;
using MessageBrokerCore.Constants;
using MessageBrokerExtensions;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues
{
    public sealed class FeedActivityChangedConsumer(
        IFeedActivityProjectionWriter projection,
        TimeProvider timeProvider) : IConsumer<FeedActivityChanged>
    {
        public async Task Consume(ConsumeContext<FeedActivityChanged> context)
        {
            await projection.ApplyCanonicalAsync(
                context.Message,
                timeProvider.GetUtcNow().UtcDateTime,
                context.CancellationToken);
        }
    }

    public sealed class FeedActivityChangedConsumerDefinition
        : EntityFrameworkOutboxConsumerDefinition<FeedActivityChangedConsumer, FeedContext>
    {
        public FeedActivityChangedConsumerDefinition()
            : base(
                Topology.Feed.ActivityChanged.Queue,
                prefetchCount: 32,
                concurrencyLimit: 16)
        {
        }
    }
}
