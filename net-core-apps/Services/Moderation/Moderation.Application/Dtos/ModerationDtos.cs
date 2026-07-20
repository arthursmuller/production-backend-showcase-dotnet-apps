using Moderation.Contracts;
using Moderation.Domain.AggregatesModel;

namespace Moderation.Application.Dtos;

public enum ModerationFailureKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    VersionConflict,
    IdempotencyConflict,
}

public sealed record ModerationFailure(
    ModerationFailureKind Kind,
    string Code,
    string Message,
    long? CurrentVersion = null);

public sealed record ModerationResult<T>(T? Value, ModerationFailure? Failure)
{
    public bool IsSuccess => Failure is null;

    public static ModerationResult<T> Success(T value) => new(value, null);
    public static ModerationResult<T> Fail(
        ModerationFailureKind kind,
        string code,
        string message,
        long? currentVersion = null) =>
        new(default, new ModerationFailure(kind, code, message, currentVersion));
}

public sealed record TriageCaseRequest(
    CasePriority Priority,
    string ReasonCode,
    long ExpectedVersion);

public sealed record AssignCaseRequest(
    string InvestigatorId,
    string ReasonCode,
    long ExpectedVersion);

public sealed record CaseTransitionRequest(
    string ReasonCode,
    long ExpectedVersion);

public sealed record DecideCaseRequest(
    ModerationDecisionAction Action,
    string ReasonCode,
    long ExpectedVersion);

public sealed record LegalHoldRequest(
    string ReasonCode,
    long ExpectedVersion);

public sealed record AddCaseMessageRequest(
    string Reference,
    string Sha256,
    string ContentType,
    string ReasonCode,
    long ExpectedVersion);

public sealed record ModerationEvidenceDetails(
    EvidenceReferenceKind Kind,
    string Reference,
    string Sha256);

public sealed record ModerationCaseDetails(
    Guid CaseId,
    Guid ReportId,
    string ReporterId,
    ModeratedResourceKind ResourceKind,
    string ResourceId,
    ReportReasonCode ReasonCode,
    string Status,
    string Priority,
    string? AssignedInvestigatorId,
    long Version,
    long DecisionVersion,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset LastChangedAtUtc,
    DateTimeOffset SlaDueAtUtc,
    DateTimeOffset? EscalatedAtUtc,
    IReadOnlyList<ModerationEvidenceDetails> EvidenceReferences);

public sealed record ModerationCaseSummary(
    Guid CaseId,
    ModeratedResourceKind ResourceKind,
    string ResourceId,
    string Status,
    string Priority,
    long Version,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset SlaDueAtUtc);

public sealed record ModerationSlaBacklogDto(
    long ActiveCount,
    long OverdueCount,
    double OldestOverdueAgeSeconds);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record AuditEntryDetails(
    Guid AuditId,
    string ActorId,
    string Action,
    string ReasonCode,
    long CaseVersion,
    string PreviousHash,
    string Hash,
    DateTimeOffset OccurredAtUtc);

public sealed record LegalHoldDetails(
    Guid HoldId,
    Guid CaseId,
    bool IsActive,
    string ReasonCode,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset? ReleasedAtUtc);

public sealed record CaseMessageDetails(
    Guid MessageId,
    Guid CaseId,
    string ActorId,
    string Reference,
    string Sha256,
    string ContentType,
    long CaseVersion,
    DateTimeOffset CreatedAtUtc);
