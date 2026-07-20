using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Application.Providers;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Commands;

public sealed class CaseMessageCommands(
    IModerationRepository repository,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public Task<ModerationResult<CaseMessageDetails>> AddCaseMessageAsync(
        Guid caseId,
        string actorId,
        AddCaseMessageRequest request,
        CancellationToken cancellationToken) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case was not found.");
            var message = moderationCase.AddMessage(
                Guid.NewGuid(),
                actorId,
                request.Reference,
                request.Sha256,
                request.ContentType,
                request.ReasonCode,
                request.ExpectedVersion,
                timeProvider.GetUtcNow());
            repository.AddMessage(message);
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return ModerationFeatureSupport.MapMessage(message);
        });

}
