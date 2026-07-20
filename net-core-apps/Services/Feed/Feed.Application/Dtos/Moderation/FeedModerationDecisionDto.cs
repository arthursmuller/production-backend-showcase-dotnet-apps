using System;

namespace Feed.Application.Dtos.Moderation;

public sealed record FeedModerationDecisionDto(
    Guid CaseId,
    string ResourceKind,
    string ResourceId,
    string Action,
    long DecisionVersion,
    DateTime OccurredAtUtc);
