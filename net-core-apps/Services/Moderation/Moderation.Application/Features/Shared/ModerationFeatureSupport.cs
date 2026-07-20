using Moderation.Application.Dtos;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Contracts;
using Moderation.Contracts.Restricted;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;

namespace Moderation.Application.Features.Shared;

internal static class ModerationFeatureSupport
{
    internal static async Task<ModerationResult<T>> ExecuteMutationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return ModerationResult<T>.Success(await operation());
        }
        catch (OperationCanceledException)
        {
            ModerationTelemetry.RecordBoundaryCancellation();
            throw;
        }
        catch (ModerationEntityNotFoundException exception)
        {
            return NotFound<T>("not-found", exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound<T>("not-found", "The moderation record was not found.");
        }
        catch (ModerationConcurrencyException exception)
        {
            ModerationTelemetry.RecordWorkflowRejection("version_conflict");
            return ModerationResult<T>.Fail(
                ModerationFailureKind.VersionConflict,
                "version-conflict",
                "The case changed; reload it and retry with the current version.",
                exception.ActualVersion);
        }
        catch (ModerationConflictException exception)
        {
            ModerationTelemetry.RecordWorkflowRejection("workflow_conflict");
            return ModerationResult<T>.Fail(
                ModerationFailureKind.Conflict,
                "workflow-conflict",
                exception.Message);
        }
        catch (ModerationValidationException exception)
        {
            return Validation<T>(exception.Message);
        }
    }

    internal static Task PublishCaseChangeAsync(
        this IModerationEventPublisher publisher,
        ModerationCase moderationCase,
        CancellationToken cancellationToken)
    {
        var audit = moderationCase.AuditEntries.Last();
        return publisher.PublishCaseChangedAsync(
            new ModerationCaseChangedRestricted(
                moderationCase.Id,
                moderationCase.Status.ToString(),
                audit.Action,
                moderationCase.Version,
                audit.OccurredAtUtc),
            cancellationToken);
    }

    internal static ModerationCaseDetails MapDetails(
        ModerationCase moderationCase,
        ModerationReport report) =>
        new(
            moderationCase.Id,
            report.Id,
            report.ReporterId,
            (ModeratedResourceKind)moderationCase.ResourceKind,
            moderationCase.ResourceId,
            (ReportReasonCode)report.Reason,
            moderationCase.Status.ToString(),
            moderationCase.Priority.ToString(),
            moderationCase.AssignedInvestigatorId,
            moderationCase.Version,
            moderationCase.DecisionVersion,
            moderationCase.ReceivedAtUtc,
            moderationCase.LastChangedAtUtc,
            moderationCase.SlaDueAtUtc,
            moderationCase.EscalatedAtUtc,
            report.EvidenceReferences.Select(item => new ModerationEvidenceDetails(
                (EvidenceReferenceKind)item.Kind,
                item.Reference,
                item.Sha256)).ToArray());

    internal static ModerationCaseSummary MapSummary(ModerationCase moderationCase) =>
        new(
            moderationCase.Id,
            (ModeratedResourceKind)moderationCase.ResourceKind,
            moderationCase.ResourceId,
            moderationCase.Status.ToString(),
            moderationCase.Priority.ToString(),
            moderationCase.Version,
            moderationCase.ReceivedAtUtc,
            moderationCase.SlaDueAtUtc);

    internal static LegalHoldDetails MapHold(LegalHold hold) =>
        new(hold.Id, hold.CaseId, hold.IsActive, hold.ReasonCode, hold.PlacedAtUtc, hold.ReleasedAtUtc);

    internal static CaseMessageDetails MapMessage(ModerationCaseMessage message) =>
        new(
            message.Id,
            message.CaseId,
            message.ActorId,
            message.Reference,
            message.Sha256,
            message.ContentType,
            message.CaseVersion,
            message.CreatedAtUtc);

    internal static string ToReporterStatus(CaseStatus status) => status switch
    {
        CaseStatus.Received => "Received",
        CaseStatus.Triaged or CaseStatus.Investigating or CaseStatus.DecisionPending or CaseStatus.Appealed => "InReview",
        CaseStatus.Decided or CaseStatus.Closed => "Resolved",
        _ => "InReview",
    };

    internal static ModerationResult<T> Validation<T>(string message) =>
        ModerationResult<T>.Fail(ModerationFailureKind.Validation, "validation-failed", message);

    internal static ModerationResult<T> NotFound<T>(string code, string message) =>
        ModerationResult<T>.Fail(ModerationFailureKind.NotFound, code, message);
}

internal sealed class ModerationEntityNotFoundException(string message) : Exception(message);
