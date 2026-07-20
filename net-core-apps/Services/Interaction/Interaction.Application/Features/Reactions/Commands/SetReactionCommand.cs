using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Reactions.Commands;

public sealed record SetReactionCommand(
    InteractionExecutionContext Context,
    IIdentityService Identity) : Command<SetReactionDto, bool>
{
    public override async Task<bool> Handle(SetReactionDto request, CancellationToken cancellationToken)
    {
        var store = Context.Repository;
        var actorId = InteractionExecutionContext.RequireActor(Identity);
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        await Context.EnsureActorAvailableAsync(actorId, cancellationToken);
        await Context.RequireAvailableResourceAsync(request.Resource, true, cancellationToken);
        var reaction = await store.GetReactionAsync(request.Resource, actorId, true, cancellationToken);
        var previous = reaction?.Kind;
        if (reaction is null)
        {
            reaction = new Reaction(request.Resource, actorId, request.Kind, Context.UtcNow());
            store.AddReaction(reaction);
        }
        else if (!reaction.SwitchTo(request.Kind, Context.UtcNow()))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var summary = await Context.GetOrCreateSummaryAsync(request.Resource, cancellationToken);
        var previousSummaryVersion = summary.Version;
        summary.ApplyReaction(previous, request.Kind, Context.UtcNow());
        await Context.Publisher.PublishReactionAsync(new ReactionChanged(
            Guid.NewGuid(), InteractionExecutionContext.ResourceTypeName(request.Resource.Type),
            request.Resource.Id, actorId, InteractionExecutionContext.Map(request.Kind),
            reaction.Version, Context.UtcNow()), cancellationToken);
        await Context.Publisher.PublishSummaryAsync(InteractionExecutionContext.ToEvent(summary), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await Context.TryInvalidateAsync(request.Resource, previousSummaryVersion, cancellationToken);
        return true;
    }
}
