using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Ratings.Commands;

public sealed record SetRatingCommand(
    InteractionExecutionContext Context,
    IIdentityService Identity) : Command<SetRatingDto, bool>
{
    public override async Task<bool> Handle(SetRatingDto request, CancellationToken cancellationToken)
    {
        var store = Context.Repository;
        var actorId = InteractionExecutionContext.RequireActor(Identity);
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        await Context.EnsureActorAvailableAsync(actorId, cancellationToken);
        await Context.RequireAvailableResourceAsync(request.Resource, true, cancellationToken);
        var rating = await store.GetRatingAsync(request.Resource, actorId, true, cancellationToken);
        var previous = rating?.Score;
        if (rating is null)
        {
            rating = new Rating(
                request.Resource, actorId, request.Score, request.Comment, Context.UtcNow());
            store.AddRating(rating);
        }
        else if (!rating.Update(request.Score, request.Comment, Context.UtcNow()))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var summary = await Context.GetOrCreateSummaryAsync(request.Resource, cancellationToken);
        var oldSummaryVersion = summary.Version;
        summary.ApplyRating(previous, request.Score, Context.UtcNow());
        await Context.Publisher.PublishRatingAsync(new RatingChanged(
            Guid.NewGuid(), InteractionExecutionContext.ResourceTypeName(request.Resource.Type),
            request.Resource.Id, actorId, request.Score, rating.Version, Context.UtcNow()),
            cancellationToken);
        await Context.Publisher.PublishSummaryAsync(InteractionExecutionContext.ToEvent(summary), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await Context.TryInvalidateAsync(request.Resource, oldSummaryVersion, cancellationToken);
        return true;
    }
}
