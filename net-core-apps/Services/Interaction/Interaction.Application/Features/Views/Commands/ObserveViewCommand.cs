using IdentityProvider;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Providers.Privacy;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;

namespace Interaction.Application.Features.Views.Commands;

public sealed record ObserveViewCommand(
    InteractionExecutionContext Context,
    IIdentityService Identity,
    IObserverKeyFactory ObserverKeyFactory) : Command<ResourceRef, bool>
{
    public override async Task<bool> Handle(ResourceRef resource, CancellationToken cancellationToken)
    {
        var actorId = InteractionExecutionContext.RequireActor(Identity);
        var store = Context.Repository;
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        await Context.EnsureActorAvailableAsync(actorId, cancellationToken);
        await Context.RequireAvailableResourceAsync(resource, false, cancellationToken);
        var now = Context.UtcNow();
        var bucketSeconds = Context.Options.ViewDedupeBucketSeconds;
        var bucket = DateTimeOffset.FromUnixTimeSeconds(
            now.ToUnixTimeSeconds() / bucketSeconds * bucketSeconds);
        var observation = new ViewObservation(
            resource,
            ObserverKeyFactory.Create(actorId, bucket),
            bucket,
            now,
            now.AddDays(Context.Options.ViewObservationRetentionDays));
        var inserted = await store.InsertViewObservationAsync(
            observation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InteractionTelemetry.ViewObservations.Add(
            1,
            new KeyValuePair<string, object?>(
                "outcome",
                inserted ? "inserted" : "deduplicated"));
        return inserted;
    }
}
