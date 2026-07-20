using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Contracts;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Commands;

public sealed class CaseCommands(
    IModerationRepository repository,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<ModerationSlaOptions> slaOptions)
{
    private readonly ModerationSlaOptions _sla = slaOptions.Value;

    public Task<ModerationResult<ModerationCaseDetails>> TriageAsync(
        Guid caseId,
        string actorId,
        TriageCaseRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCaseMutationAsync(caseId, cancellationToken, moderationCase =>
        {
            var now = timeProvider.GetUtcNow();
            moderationCase.Triage(
                actorId,
                request.ReasonCode,
                request.Priority,
                request.ExpectedVersion,
                now,
                now.AddMinutes(PriorityMinutes(request.Priority)));
        }, moderationCase => ModerationTelemetry.RecordFirstResponse(
            moderationCase.LastChangedAtUtc - moderationCase.ReceivedAtUtc));

    public Task<ModerationResult<ModerationCaseDetails>> AssignAsync(
        Guid caseId,
        string actorId,
        AssignCaseRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCaseMutationAsync(caseId, cancellationToken, moderationCase =>
            moderationCase.Assign(
                actorId,
                request.InvestigatorId,
                request.ReasonCode,
                request.ExpectedVersion,
                timeProvider.GetUtcNow()));

    public Task<ModerationResult<ModerationCaseDetails>> StartInvestigationAsync(
        Guid caseId,
        string actorId,
        CaseTransitionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCaseMutationAsync(caseId, cancellationToken, moderationCase =>
        {
            var now = timeProvider.GetUtcNow();
            moderationCase.StartInvestigation(
                actorId,
                request.ReasonCode,
                request.ExpectedVersion,
                now,
                now.AddMinutes(_sla.InvestigationMinutes));
        });

    public Task<ModerationResult<ModerationCaseDetails>> CompleteInvestigationAsync(
        Guid caseId,
        string actorId,
        CaseTransitionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCaseMutationAsync(caseId, cancellationToken, moderationCase =>
        {
            var now = timeProvider.GetUtcNow();
            moderationCase.CompleteInvestigation(
                actorId,
                request.ReasonCode,
                request.ExpectedVersion,
                now,
                now.AddMinutes(_sla.DecisionMinutes));
        });

    public Task<ModerationResult<ModerationCaseDetails>> CloseAsync(
        Guid caseId,
        string actorId,
        CaseTransitionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteCaseMutationAsync(caseId, cancellationToken, moderationCase =>
            moderationCase.Close(
                actorId,
                request.ReasonCode,
                request.ExpectedVersion,
                timeProvider.GetUtcNow()));

    public Task<ModerationResult<ModerationCaseDetails>> DecideAsync(
        Guid caseId,
        string actorId,
        DecideCaseRequest request,
        CancellationToken cancellationToken) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case was not found.");
            var now = timeProvider.GetUtcNow();
            moderationCase.Decide(
                actorId,
                request.ReasonCode,
                (DecisionAction)request.Action,
                request.ExpectedVersion,
                now);

            var decision = new ModerationDecisionMade(
                moderationCase.Id,
                (ModeratedResourceKind)moderationCase.ResourceKind,
                moderationCase.ResourceId,
                request.Action,
                moderationCase.DecisionVersion,
                now);
            await publisher.PublishDecisionAsync(decision, cancellationToken);
            if (request.Action != ModerationDecisionAction.NoAction)
            {
                await publisher.PublishActionAsync(
                    new ModerationActionApplied(
                        decision.CaseId,
                        decision.ResourceKind,
                        decision.ResourceId,
                        decision.Action,
                        decision.DecisionVersion,
                        decision.OccurredAtUtc),
                    cancellationToken);
            }

            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            ModerationTelemetry.Decisions.Add(
                1,
                new KeyValuePair<string, object?>("action", request.Action.ToString()));
            var report = await repository.GetReportAsync(moderationCase.ReportId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case report was not found.");
            return ModerationFeatureSupport.MapDetails(moderationCase, report);
        });

    private Task<ModerationResult<ModerationCaseDetails>> ExecuteCaseMutationAsync(
        Guid caseId,
        CancellationToken cancellationToken,
        Action<ModerationCase> mutate,
        Action<ModerationCase>? afterSave = null) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case was not found.");
            mutate(moderationCase);
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            afterSave?.Invoke(moderationCase);
            var report = await repository.GetReportAsync(moderationCase.ReportId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The case report was not found.");
            return ModerationFeatureSupport.MapDetails(moderationCase, report);
        });

    private int PriorityMinutes(CasePriority priority) => priority switch
    {
        CasePriority.Low => _sla.LowPriorityMinutes,
        CasePriority.Normal => _sla.NormalPriorityMinutes,
        CasePriority.High => _sla.HighPriorityMinutes,
        CasePriority.Critical => _sla.CriticalPriorityMinutes,
        _ => throw new ModerationValidationException("The priority is not allow-listed."),
    };
}
