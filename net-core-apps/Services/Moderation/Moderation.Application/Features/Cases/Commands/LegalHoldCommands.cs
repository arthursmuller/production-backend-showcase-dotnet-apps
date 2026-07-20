using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Application.Providers;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Commands;

public sealed class LegalHoldCommands(
    IModerationRepository repository,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public Task<ModerationResult<LegalHoldDetails>> PlaceLegalHoldAsync(
        Guid caseId,
        string actorId,
        LegalHoldRequest request,
        CancellationToken cancellationToken) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case was not found.");
            if (await repository.GetActiveLegalHoldAsync(caseId, cancellationToken) is not null)
                throw new ModerationConflictException("An active record-scoped legal hold already exists.");
            var now = timeProvider.GetUtcNow();
            var hold = LegalHold.Place(
                caseId,
                new ResourceReference(moderationCase.ResourceKind, moderationCase.ResourceId),
                actorId,
                request.ReasonCode,
                now);
            moderationCase.RecordLegalHold(actorId, request.ReasonCode, true, request.ExpectedVersion, now);
            repository.AddLegalHold(hold);
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return ModerationFeatureSupport.MapHold(hold);
        });

    public Task<ModerationResult<LegalHoldDetails>> ReleaseLegalHoldAsync(
        Guid caseId,
        string actorId,
        LegalHoldRequest request,
        CancellationToken cancellationToken) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case was not found.");
            var hold = await repository.GetActiveLegalHoldAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("No active legal hold was found for this case.");
            var now = timeProvider.GetUtcNow();
            moderationCase.RecordLegalHold(actorId, request.ReasonCode, false, request.ExpectedVersion, now);
            hold.Release(actorId, request.ReasonCode, now);
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return ModerationFeatureSupport.MapHold(hold);
        });
}
