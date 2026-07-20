using Feed.Application.Dtos.Moderation;
using Feed.Application.Features.Moderation;
using Feed.Infra.Persistence;
using MassTransit;
using Moderation.Contracts;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues;

public sealed class ModerationDecisionMadeConsumer(
    ApplyFeedModerationDecisionCommand command) : IConsumer<ModerationDecisionMade>
{
    public Task Consume(ConsumeContext<ModerationDecisionMade> context) =>
        command.HandleAsync(Map(context.Message), context.CancellationToken);

    internal static FeedModerationDecisionDto Map(ModerationDecisionMade message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new FeedModerationDecisionDto(
            message.CaseId,
            Map(message.ResourceKind),
            message.ResourceId,
            Map(message.Action),
            message.DecisionVersion,
            message.OccurredAtUtc.UtcDateTime);
    }

    internal static string Map(ModeratedResourceKind kind) => kind switch
    {
        ModeratedResourceKind.Post => "post",
        ModeratedResourceKind.Comment => "comment",
        ModeratedResourceKind.Profile => "profile",
        ModeratedResourceKind.Message => "message",
        ModeratedResourceKind.Media => "media",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string Map(ModerationDecisionAction action) => action switch
    {
        ModerationDecisionAction.NoAction => "no_action",
        ModerationDecisionAction.LimitDistribution => "limit_distribution",
        ModerationDecisionAction.RemoveContent => "remove_content",
        ModerationDecisionAction.SuspendAccount => "suspend_account",
        ModerationDecisionAction.PreserveForReview => "preserve_for_review",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}

public sealed class ModerationActionAppliedConsumer(
    ApplyFeedModerationDecisionCommand command) : IConsumer<ModerationActionApplied>
{
    public Task Consume(ConsumeContext<ModerationActionApplied> context)
        => command.HandleAsync(Map(context.Message), context.CancellationToken);

    internal static FeedModerationDecisionDto Map(ModerationActionApplied message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new FeedModerationDecisionDto(
            message.CaseId,
            ModerationDecisionMadeConsumer.Map(message.ResourceKind),
            message.ResourceId,
            ModerationDecisionMadeConsumer.Map(message.Action),
            message.DecisionVersion,
            message.OccurredAtUtc.UtcDateTime);
    }
}

public sealed class ModerationDecisionMadeConsumerDefinition
    : ConsumerDefinition<ModerationDecisionMadeConsumer>
{
    public ModerationDecisionMadeConsumerDefinition()
    {
        EndpointName = "feed-moderation-decision";
        ConcurrentMessageLimit = 16;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ModerationDecisionMadeConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        Configure(endpointConfigurator);
        endpointConfigurator.UseEntityFrameworkOutbox<FeedContext>(context);
    }

    internal static void Configure(IReceiveEndpointConfigurator endpointConfigurator)
    {
        endpointConfigurator.PrefetchCount = 32;
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1)));
        endpointConfigurator.UseDelayedRedelivery(redelivery => redelivery.Intervals(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30)));
    }
}

public sealed class ModerationActionAppliedConsumerDefinition
    : ConsumerDefinition<ModerationActionAppliedConsumer>
{
    public ModerationActionAppliedConsumerDefinition()
    {
        EndpointName = "feed-moderation-action";
        ConcurrentMessageLimit = 16;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ModerationActionAppliedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        ModerationDecisionMadeConsumerDefinition.Configure(endpointConfigurator);
        endpointConfigurator.UseEntityFrameworkOutbox<FeedContext>(context);
    }
}
