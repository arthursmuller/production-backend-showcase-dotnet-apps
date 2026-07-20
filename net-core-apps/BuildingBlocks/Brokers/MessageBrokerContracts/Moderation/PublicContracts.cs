namespace Moderation.Contracts;

public enum ModeratedResourceKind
{
    Post = 1,
    Comment = 2,
    Profile = 3,
    Message = 4,
    Media = 5,
}

public enum EvidenceReferenceKind
{
    ObjectStorage = 1,
    ContentSnapshot = 2,
    ExternalCase = 3,
}

public enum ReportReasonCode
{
    Spam = 1,
    Harassment = 2,
    Hate = 3,
    Violence = 4,
    SexualContent = 5,
    SelfHarm = 6,
    Fraud = 7,
    Impersonation = 8,
    Privacy = 9,
    OtherPolicyViolation = 10,
}

public enum ModerationDecisionAction
{
    NoAction = 1,
    LimitDistribution = 2,
    RemoveContent = 3,
    SuspendAccount = 4,
    PreserveForReview = 5,
}

/// <summary>
/// Public event intentionally limited to the affected resource, action and monotonic version.
/// Reporter, evidence, assignment, rationale, and free-form text are prohibited.
/// </summary>
public sealed record ModerationDecisionMade(
    Guid CaseId,
    ModeratedResourceKind ResourceKind,
    string ResourceId,
    ModerationDecisionAction Action,
    long DecisionVersion,
    DateTimeOffset OccurredAtUtc);

/// <summary>Idempotent downstream action coordinate; contains no case evidence or rationale.</summary>
public sealed record ModerationActionApplied(
    Guid CaseId,
    ModeratedResourceKind ResourceKind,
    string ResourceId,
    ModerationDecisionAction Action,
    long DecisionVersion,
    DateTimeOffset OccurredAtUtc);
