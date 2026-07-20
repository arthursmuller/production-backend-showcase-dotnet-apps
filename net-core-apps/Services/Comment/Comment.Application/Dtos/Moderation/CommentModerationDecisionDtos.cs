using System;

namespace Comment.Application.Dtos.Moderation;

public enum CommentModeratedResourceKind
{
    Post = 1,
    Comment = 2,
    Profile = 3,
    Message = 4,
    Media = 5,
}

public enum CommentModerationDecisionKind
{
    NoAction = 1,
    LimitDistribution = 2,
    RemoveContent = 3,
    SuspendAccount = 4,
    PreserveForReview = 5,
}

/// <summary>
/// Transport-neutral public decision coordinate. It deliberately has no fields
/// for evidence, reporter identity, rationale, or other case material.
/// </summary>
public sealed record ApplyCommentModerationDecisionRequest(
    Guid CaseId,
    CommentModeratedResourceKind ResourceKind,
    string ResourceId,
    CommentModerationDecisionKind Action,
    long DecisionVersion,
    DateTimeOffset OccurredAtUtc);

public enum ApplyCommentModerationDecisionOutcome
{
    NotApplicable = 0,
    Ignored = 1,
    Applied = 2,
    GapBuffered = 3,
    GapBufferFull = 4,
}

public sealed record ApplyCommentModerationDecisionResponse(
    ApplyCommentModerationDecisionOutcome Outcome,
    bool IsSuppressed,
    long AppliedVersion,
    long HighestSeenVersion,
    int BufferedGapCount);
