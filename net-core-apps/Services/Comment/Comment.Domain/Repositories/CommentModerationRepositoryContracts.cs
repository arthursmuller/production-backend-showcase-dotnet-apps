using Comment.Domain.AggregatesModel.Moderation;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Domain.Repositories;

public enum CommentModerationApplyOutcome
{
    Ignored = 0,
    Applied = 1,
    GapBuffered = 2,
    GapBufferFull = 3,
}

public sealed record CommentModerationApplyResult(
    CommentModerationApplyOutcome Outcome,
    long AppliedVersion,
    long HighestSeenVersion,
    bool IsSuppressed,
    int BufferedGapCount);

public interface ICommentModerationDecisionRepository
{
    Task<CommentModerationApplyResult> ApplyAsync(
        Guid caseId,
        int commentId,
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);
}
