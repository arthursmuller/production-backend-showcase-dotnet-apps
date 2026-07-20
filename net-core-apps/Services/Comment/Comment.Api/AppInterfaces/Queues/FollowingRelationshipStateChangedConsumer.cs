using Comment.Application.Features.Visibility;
using Comment.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Following;
using MessageBrokerExtensions;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Queues
{
    /// <summary>Projection propagation is unconditional; the read activation gate is separate.</summary>
    public sealed class FollowingRelationshipStateChangedConsumer(
        ICommentRelationshipProjectionWriter projection,
        System.TimeProvider timeProvider) : IConsumer<RelationshipStateChanged>
    {
        public Task Consume(ConsumeContext<RelationshipStateChanged> context) =>
            projection.ApplyAsync(context.Message, timeProvider.GetUtcNow().UtcDateTime,
                context.CancellationToken);
    }

    public sealed class FollowingRelationshipStateChangedConsumerDefinition
        : EntityFrameworkOutboxConsumerDefinition<FollowingRelationshipStateChangedConsumer, CommentContext>
    {
        public FollowingRelationshipStateChangedConsumerDefinition()
            : base(
                "comment_following_relationship_state_queue",
                prefetchCount: 64,
                concurrencyLimit: 32)
        {
        }
    }
}
