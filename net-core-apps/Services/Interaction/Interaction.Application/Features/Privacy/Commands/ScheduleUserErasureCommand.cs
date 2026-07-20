using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using MessageBrokerContracts.Privacy.Restricted;

namespace Interaction.Application.Features.Privacy.Commands;

public sealed record ScheduleUserErasureCommand(
    InteractionExecutionContext Context,
    IInteractionErasureRepository Erasures)
    : Command<UserErasureRequested, bool>
{
    public override async Task<bool> Handle(
        UserErasureRequested request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EventId == Guid.Empty
            || request.RequestId == Guid.Empty
            || request.UserId <= 0
            || request.ErasureVersion <= 0
            || request.RequestedAtUtc < DateTimeOffset.UnixEpoch
            || request.RequestedAtUtc > Context.UtcNow().AddMinutes(5))
        {
            throw new InteractionValidationException("The user-erasure request is invalid.");
        }

        var store = Context.Repository;
        await using var transaction = await store.BeginSerializableAsync(cancellationToken);
        var current = await Erasures.GetForUpdateAsync(request.UserId, cancellationToken);
        // The command may be delivered late. Wait a full raw-view window from local receipt,
        // not merely from the source timestamp, so opaque observations written before this
        // tombstone are guaranteed to age out before completion is acknowledged.
        var rawViewsExpireAt = Context.UtcNow().AddDays(
            Context.Options.ViewObservationRetentionDays);
        var outcome = "scheduled";
        if (current is null)
        {
            Erasures.Add(InteractionUserErasure.Schedule(
                request.UserId,
                request.RequestId,
                request.ErasureVersion,
                request.RequestedAtUtc,
                rawViewsExpireAt));
        }
        else if (request.ErasureVersion < current.ErasureVersion)
        {
            outcome = "stale";
            await PublishCompletionAsync(
                request.RequestId,
                request.UserId,
                request.ErasureVersion,
                UserErasureOutcomes.Superseded,
                cancellationToken);
        }
        else if (request.ErasureVersion == current.ErasureVersion)
        {
            if (request.RequestId != current.RequestId)
                throw new InteractionConflictException(
                    "An erasure version cannot identify two requests.");
            await transaction.CommitAsync(cancellationToken);
            InteractionTelemetry.ErasureRequests.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "idempotent"));
            return false;
        }
        else
        {
            outcome = "superseding";
            if (current.Stage != InteractionUserErasureStage.Completed)
            {
                await PublishCompletionAsync(
                    current.RequestId,
                    current.UserId,
                    current.ErasureVersion,
                    UserErasureOutcomes.Superseded,
                    cancellationToken);
            }
            current.Supersede(
                request.RequestId,
                request.ErasureVersion,
                request.RequestedAtUtc,
                rawViewsExpireAt);
        }

        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InteractionTelemetry.ErasureRequests.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));
        return true;
    }

    private Task PublishCompletionAsync(
        Guid requestId,
        long userId,
        long erasureVersion,
        string outcome,
        CancellationToken cancellationToken) =>
        Context.Publisher.PublishErasureCompletedAsync(
            new UserErasureCompleted
            {
                EventId = Guid.NewGuid(),
                RequestId = requestId,
                UserId = userId,
                ErasureVersion = erasureVersion,
                Service = "interaction",
                Outcome = outcome,
                CompletedAtUtc = Context.UtcNow(),
            },
            cancellationToken);
}
