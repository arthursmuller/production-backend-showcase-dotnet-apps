using Feed.Application.Dtos;
using Feed.Application.Features.Engagement;
using Feed.Infra.Persistence;
using Interaction.Contracts;
using MassTransit;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues
{
    public sealed class EngagementSummaryChangedConsumer(
        ProjectEngagementSummaryCommand command)
        : IConsumer<EngagementSummaryChanged>
    {
        public Task Consume(ConsumeContext<EngagementSummaryChanged> context)
        {
            var message = context.Message;
            return command.Handle(
                Map(message),
                context.CancellationToken);
        }

        internal static EngagementSummaryProjectionDto Map(
            EngagementSummaryChanged message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return new EngagementSummaryProjectionDto(
                message.EventId,
                message.ResourceType,
                message.ResourceId,
                message.LikeCount,
                message.DislikeCount,
                message.RatingCount,
                message.RatingTotal,
                message.ViewCount,
                message.CommentCount,
                message.ShareCount,
                message.SummaryVersion,
                message.OccurredAtUtc.UtcDateTime);
        }
    }

    /// <summary>
    /// Uses MassTransit's native publish topology for the shared Interaction contract and the
    /// Feed DbContext consumer outbox for atomic inbox/projection persistence.
    /// </summary>
    public sealed class EngagementSummaryChangedConsumerDefinition
        : ConsumerDefinition<EngagementSummaryChangedConsumer>
    {
        public EngagementSummaryChangedConsumerDefinition()
        {
            EndpointName = "feed-interaction-engagement-summary";
            ConcurrentMessageLimit = 16;
        }

        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<EngagementSummaryChangedConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            endpointConfigurator.PrefetchCount = 32;
            endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromSeconds(1)));
            endpointConfigurator.UseDelayedRedelivery(redelivery => redelivery.Intervals(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30)));
            endpointConfigurator.UseEntityFrameworkOutbox<FeedContext>(context);
        }
    }
}
