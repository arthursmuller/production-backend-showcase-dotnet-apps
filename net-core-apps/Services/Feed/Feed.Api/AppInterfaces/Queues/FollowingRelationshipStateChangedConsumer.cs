using Feed.Application.Features.Audience;
using Feed.Application.Features.Feeds;
using Feed.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Following;
using MessageBrokerCore.Constants;
using MessageBrokerExtensions;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues
{
    public sealed class FollowingRelationshipStateChangedConsumer(
        IRelationshipProjectionWriter projection,
        TimeProvider timeProvider) : IConsumer<RelationshipStateChanged>
    {
        public async Task Consume(ConsumeContext<RelationshipStateChanged> context)
        {
            await projection.ApplyAsync(
                context.Message,
                timeProvider.GetUtcNow().UtcDateTime,
                context.CancellationToken);
        }
    }

    public sealed class FollowingRelationshipStateChangedConsumerDefinition
        : EntityFrameworkOutboxConsumerDefinition<FollowingRelationshipStateChangedConsumer, FeedContext>
    {
        public FollowingRelationshipStateChangedConsumerDefinition()
            : base(
                Topology.Following.RelationshipStateChanged.Queue,
                prefetchCount: 64,
                concurrencyLimit: 32)
        {
        }
    }
}
