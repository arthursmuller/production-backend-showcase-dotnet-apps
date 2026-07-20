namespace Moderation.Contracts.Restricted;

/// <summary>
/// Restricted operational signal. This contract is for the Moderation operations boundary and
/// deliberately omits reporter identity, evidence, rationale, and content.
/// </summary>
public sealed record ModerationCaseEscalatedRestricted(
    Guid CaseId,
    string Priority,
    long CaseVersion,
    DateTimeOffset EscalatedAtUtc);

/// <summary>Low-cardinality restricted workflow signal; excludes actor, rationale and evidence.</summary>
public sealed record ModerationCaseChangedRestricted(
    Guid CaseId,
    string Status,
    string Action,
    long CaseVersion,
    DateTimeOffset OccurredAtUtc);
