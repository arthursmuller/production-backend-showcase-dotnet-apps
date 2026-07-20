using Interaction.Application.Dtos;
using Interaction.Application.Features.Projections.Commands;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;
using Interaction.Infraestructure.Database.Ef;
using MassTransit;
using ContractResourceLifecycle = Interaction.Contracts.ResourceLifecycle;
using ContractResourceVisibility = Interaction.Contracts.ResourceVisibility;
using DomainResourceLifecycle = Interaction.Domain.AggregatesModel.ResourceLifecycle;
using DomainResourceVisibility = Interaction.Domain.AggregatesModel.ResourceVisibility;

namespace Interaction.Api.AppInterfaces.Queues;

public sealed class CommentResourceStateChangedConsumer(ApplyResourceStateCommand command)
    : IConsumer<CommentResourceStateChanged>
{
    public Task Consume(ConsumeContext<CommentResourceStateChanged> context) => command.Handle(
        new ResourceStateUpdate(
            new ResourceRef(ResourceType.Comment, context.Message.CommentId),
            Map(context.Message.Lifecycle), Map(context.Message.Visibility),
            context.Message.SourceVersion, context.Message.OccurredAtUtc),
        context.CancellationToken);

    private static DomainResourceLifecycle Map(ContractResourceLifecycle value) => value switch
    {
        ContractResourceLifecycle.Active => DomainResourceLifecycle.Active,
        ContractResourceLifecycle.Deleted => DomainResourceLifecycle.Deleted,
        _ => DomainResourceLifecycle.Unknown,
    };
    private static DomainResourceVisibility Map(ContractResourceVisibility value) => value switch
    {
        ContractResourceVisibility.Public => DomainResourceVisibility.Public,
        ContractResourceVisibility.Restricted => DomainResourceVisibility.Restricted,
        _ => DomainResourceVisibility.Private,
    };
}

public sealed class PostResourceStateChangedConsumer(ApplyResourceStateCommand command)
    : IConsumer<PostResourceStateChanged>
{
    public Task Consume(ConsumeContext<PostResourceStateChanged> context) => command.Handle(
        new ResourceStateUpdate(
            new ResourceRef(ResourceType.Post, context.Message.PostId),
            Map(context.Message.Lifecycle), Map(context.Message.Visibility),
            context.Message.SourceVersion, context.Message.OccurredAtUtc),
        context.CancellationToken);

    private static DomainResourceLifecycle Map(ContractResourceLifecycle value) =>
        value == ContractResourceLifecycle.Active
            ? DomainResourceLifecycle.Active
            : DomainResourceLifecycle.Deleted;
    private static DomainResourceVisibility Map(ContractResourceVisibility value) => value switch
    {
        ContractResourceVisibility.Public => DomainResourceVisibility.Public,
        ContractResourceVisibility.Restricted => DomainResourceVisibility.Restricted,
        _ => DomainResourceVisibility.Private,
    };
}

public sealed class ResourceContributionChangedConsumer(ApplyContributionCommand command)
    : IConsumer<ResourceContributionChanged>
{
    public Task Consume(ConsumeContext<ResourceContributionChanged> context) => command.Handle(
        new ResourceContributionUpdate(
            ResourceRef.Parse(context.Message.ResourceType, context.Message.ResourceId),
            context.Message.CommentCount, context.Message.ShareCount,
            context.Message.SourceVersion, context.Message.OccurredAtUtc),
        context.CancellationToken);
}

public abstract class InteractionConsumerDefinition<TConsumer> : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    protected InteractionConsumerDefinition(string endpointName)
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
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
        endpointConfigurator.UseDelayedRedelivery(redelivery => redelivery.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        endpointConfigurator.UseEntityFrameworkOutbox<InteractionDbContext>(context);
    }
}

public sealed class CommentResourceStateChangedConsumerDefinition()
    : InteractionConsumerDefinition<CommentResourceStateChangedConsumer>("interaction-comment-resource-state");
public sealed class PostResourceStateChangedConsumerDefinition()
    : InteractionConsumerDefinition<PostResourceStateChangedConsumer>("interaction-post-resource-state");
public sealed class ResourceContributionChangedConsumerDefinition()
    : InteractionConsumerDefinition<ResourceContributionChangedConsumer>("interaction-resource-contribution");
