using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.Extensions.Options;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Privacy.Commands;

public sealed class ProcessModerationUserErasureCommand(
    IModerationUserErasureStore store,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<ModerationErasureOptions> options)
{
    private readonly ModerationErasureOptions _options = options.Value;

    public async Task<int> Handle(
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return 0;

        var now = timeProvider.GetUtcNow();
        await using var transaction =
            await store.BeginSerializableAsync(cancellationToken);
        var erasure = await store.LockNextDueAsync(now, cancellationToken);
        if (erasure is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var fencingToken = erasure.Acquire(
            leaseOwner,
            now,
            TimeSpan.FromSeconds(_options.LeaseSeconds));
        await store.SaveChangesAsync(cancellationToken);

        var batch = await store.ExecuteDataBatchAsync(
            erasure.UserId,
            fencingToken,
            _options.MaxBatchSize,
            cancellationToken);
        var hasDeletableData = await store.HasDeletableDataAsync(
            erasure.UserId,
            cancellationToken);
        var heldCases = await store.CountActiveHeldCasesAsync(
            erasure.UserId,
            cancellationToken);

        var completedNow = false;
        if (hasDeletableData)
        {
            erasure.Continue(now);
        }
        else
        {
            completedNow = erasure.Complete(
                now,
                heldCases,
                TimeSpan.FromSeconds(_options.LegalHoldReviewSeconds));
            if (completedNow)
            {
                await publisher.PublishErasureCompletedAsync(
                    new UserErasureCompleted
                    {
                        EventId = ModerationErasureEventIds.Create(
                            erasure.RequestId,
                            erasure.ErasureVersion,
                            UserErasureOutcomes.Completed),
                        RequestId = erasure.RequestId,
                        UserId = erasure.UserId,
                        ErasureVersion = erasure.ErasureVersion,
                        Service = "moderation",
                        Outcome = UserErasureOutcomes.Completed,
                        CompletedAtUtc = now,
                    },
                    cancellationToken);
            }
        }

        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var workUnits = batch.WorkUnits
            + (hasDeletableData || completedNow ? 1 : 0);
        ModerationTelemetry.ErasureBatchWork.Record(workUnits);
        ModerationTelemetry.ErasureHeldCases.Record(heldCases);
        if (completedNow)
        {
            ModerationTelemetry.ErasureCompletions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "completed"));
        }
        return workUnits;
    }
}
