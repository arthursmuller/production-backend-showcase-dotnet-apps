using IdentityProvider;
using Interaction.Application.Providers.Execution;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Reactions.Commands;

public sealed record RemoveReactionCommand(
    InteractionExecutionContext Context,
    IIdentityService Identity) : Command<ResourceRef, bool>
{
    public override async Task<bool> Handle(ResourceRef resource, CancellationToken cancellationToken)
    {
        var store = Context.Repository;
        var actorId = InteractionExecutionContext.RequireActor(Identity);
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        await Context.EnsureActorAvailableAsync(actorId, cancellationToken);
        await Context.RequireAvailableResourceAsync(resource, true, cancellationToken);
        var reaction = await store.GetReactionAsync(resource, actorId, true, cancellationToken);
        if (reaction is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var summary = await Context.GetOrCreateSummaryAsync(resource, cancellationToken);
        var oldSummaryVersion = summary.Version;
        summary.ApplyReaction(reaction.Kind, null, Context.UtcNow());
        store.RemoveReaction(reaction);
        await Context.Publisher.PublishReactionAsync(new ReactionChanged(
            Guid.NewGuid(), InteractionExecutionContext.ResourceTypeName(resource.Type), resource.Id,
            actorId, null, reaction.Version + 1, Context.UtcNow()), cancellationToken);
        await Context.Publisher.PublishSummaryAsync(InteractionExecutionContext.ToEvent(summary), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await Context.TryInvalidateAsync(resource, oldSummaryVersion, cancellationToken);
        return true;
    }
}
