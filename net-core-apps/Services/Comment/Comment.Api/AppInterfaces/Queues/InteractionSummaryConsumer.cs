using Comment.Application.Features.Interactions;
using Comment.Application.Providers.Interactions;
using Comment.Application.Dtos.Interactions;
using Comment.Infra.Persistence;
using Interaction.Contracts;
using MassTransit;
using System;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Queues
{
    public sealed class InteractionSummaryConsumer(ICommentInteractionProjectionStore projections)
        : IConsumer<EngagementSummaryChanged>
    {
        public Task Consume(ConsumeContext<EngagementSummaryChanged> context) =>
            projections.ApplySummaryAsync(context.Message, context.CancellationToken);
    }

    public sealed class InteractionReactionConsumer(ICommentInteractionProjectionStore projections)
        : IConsumer<ReactionChanged>
    {
        public Task Consume(ConsumeContext<ReactionChanged> context) =>
            projections.ApplyReactionAsync(context.Message, context.CancellationToken);
    }

    public sealed class InteractionRatingConsumer(ICommentInteractionProjectionStore projections)
        : IConsumer<RatingChanged>
    {
        public Task Consume(ConsumeContext<RatingChanged> context) =>
            projections.ApplyRatingAsync(context.Message, context.CancellationToken);
    }

    public sealed class InteractionSummaryConsumerDefinition
        : ConsumerDefinition<InteractionSummaryConsumer>
    {
        public InteractionSummaryConsumerDefinition()
        {
            EndpointName = "comment-interaction-summary";
            ConcurrentMessageLimit = 16;
        }

        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<InteractionSummaryConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            endpointConfigurator.PrefetchCount = 32;
            endpointConfigurator.UseMessageRetry(retry =>
                retry.Intervals(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
            endpointConfigurator.UseDelayedRedelivery(redelivery =>
                redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
            endpointConfigurator.UseEntityFrameworkOutbox<CommentContext>(context);
        }
    }

    public sealed class InteractionReactionConsumerDefinition
        : ConsumerDefinition<InteractionReactionConsumer>
    {
        public InteractionReactionConsumerDefinition()
        {
            EndpointName = "comment-interaction-reaction";
            ConcurrentMessageLimit = 16;
        }

        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<InteractionReactionConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            endpointConfigurator.PrefetchCount = 32;
            endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
            endpointConfigurator.UseDelayedRedelivery(redelivery =>
                redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
            endpointConfigurator.UseEntityFrameworkOutbox<CommentContext>(context);
        }
    }

    public sealed class InteractionRatingConsumerDefinition
        : ConsumerDefinition<InteractionRatingConsumer>
    {
        public InteractionRatingConsumerDefinition()
        {
            EndpointName = "comment-interaction-rating";
            ConcurrentMessageLimit = 16;
        }

        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<InteractionRatingConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            endpointConfigurator.PrefetchCount = 32;
            endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
            endpointConfigurator.UseDelayedRedelivery(redelivery =>
                redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
            endpointConfigurator.UseEntityFrameworkOutbox<CommentContext>(context);
        }
    }
}
