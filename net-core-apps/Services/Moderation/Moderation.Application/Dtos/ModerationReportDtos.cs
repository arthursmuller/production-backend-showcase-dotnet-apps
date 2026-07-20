namespace Moderation.Application.Dtos;

/// <summary>
/// Application-owned HTTP resource kinds. Numeric values and names intentionally match the
/// canonical wire contract without coupling the HTTP boundary to broker contracts.
/// </summary>
public enum ModeratedResourceKindDto
{
    Post = 1,
    Comment = 2,
    Profile = 3,
    Message = 4,
    Media = 5,
}

/// <summary>Application-owned HTTP evidence-reference kinds.</summary>
public enum EvidenceReferenceKindDto
{
    ObjectStorage = 1,
    ContentSnapshot = 2,
    ExternalCase = 3,
}

/// <summary>Application-owned, allow-listed HTTP report reasons.</summary>
public enum ReportReasonCodeDto
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

/// <summary>A typed, allow-listed coordinate. It is never an arbitrary URL.</summary>
public sealed record ModeratedResourceReferenceDto(
    ModeratedResourceKindDto Kind,
    string ResourceId);

/// <summary>
/// An opaque reference to separately protected evidence. Evidence bytes are not accepted by the
/// Moderation HTTP API.
/// </summary>
public sealed record EvidenceReferenceDto(
    EvidenceReferenceKindDto Kind,
    string Reference,
    string Sha256);

public sealed record SubmitModerationReportRequestDto(
    ModeratedResourceReferenceDto Resource,
    ReportReasonCodeDto ReasonCode,
    IReadOnlyList<EvidenceReferenceDto> EvidenceReferences);

public sealed record ModerationReportReceiptDto(
    Guid ReportId,
    string Status,
    DateTimeOffset ReceivedAtUtc,
    bool IsDuplicate);

public sealed record ModerationReportStatusDto(
    Guid ReportId,
    string Status,
    DateTimeOffset LastChangedAtUtc,
    bool CanAppeal,
    long Version);
