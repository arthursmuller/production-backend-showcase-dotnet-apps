using Comment.Application.Dtos.Moderation;
using Comment.Application.Features.Moderation.Commands;
using Comment.Infra.Persistence;
using MassTransit;
using Moderation.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Queues;

public sealed class ModerationDecisionMadeConsumer(
    ApplyCommentModerationDecisionCommand command)
    : IConsumer<ModerationDecisionMade>
{
    public Task Consume(ConsumeContext<ModerationDecisionMade> context) =>
        ConsumeMessageAsync(context.Message, context.CancellationToken);

    public async Task ConsumeMessageAsync(
        ModerationDecisionMade message,
        CancellationToken cancellationToken) =>
        _ = await command.ExecuteAsync(Map(message), cancellationToken);

    private static ApplyCommentModerationDecisionRequest Map(
        ModerationDecisionMade message) => new(
        message.CaseId,
        (CommentModeratedResourceKind)message.ResourceKind,
        message.ResourceId,
        (CommentModerationDecisionKind)message.Action,
        message.DecisionVersion,
        message.OccurredAtUtc);
}

public sealed class ModerationActionAppliedConsumer(
    ApplyCommentModerationDecisionCommand command)
    : IConsumer<ModerationActionApplied>
{
    public Task Consume(ConsumeContext<ModerationActionApplied> context) =>
        ConsumeMessageAsync(context.Message, context.CancellationToken);

    public async Task ConsumeMessageAsync(
        ModerationActionApplied message,
        CancellationToken cancellationToken) =>
        _ = await command.ExecuteAsync(Map(message), cancellationToken);

    private static ApplyCommentModerationDecisionRequest Map(
        ModerationActionApplied message) => new(
        message.CaseId,
        (CommentModeratedResourceKind)message.ResourceKind,
        message.ResourceId,
        (CommentModerationDecisionKind)message.Action,
        message.DecisionVersion,
        message.OccurredAtUtc);
}

public abstract class CommentModerationConsumerDefinition<TConsumer>
    : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    protected CommentModerationConsumerDefinition(string endpointName)
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
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1)));
        endpointConfigurator.UseDelayedRedelivery(redelivery =>
            redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        endpointConfigurator.UseEntityFrameworkOutbox<CommentContext>(context);
    }
}

public sealed class ModerationDecisionMadeConsumerDefinition()
    : CommentModerationConsumerDefinition<ModerationDecisionMadeConsumer>(
        "comment-moderation-decision-made");

public sealed class ModerationActionAppliedConsumerDefinition()
    : CommentModerationConsumerDefinition<ModerationActionAppliedConsumer>(
        "comment-moderation-action-applied");
