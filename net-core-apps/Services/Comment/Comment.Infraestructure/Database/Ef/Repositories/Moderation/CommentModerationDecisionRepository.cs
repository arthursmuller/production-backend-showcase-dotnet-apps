using Comment.Domain.AggregatesModel.Moderation;
using Comment.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories.Moderation;

public sealed class CommentModerationDecisionRepository(CommentContext context)
    : ICommentModerationDecisionRepository
{
    public const int MaximumBufferedGapsPerCase = 64;

    public async Task<CommentModerationApplyResult> ApplyAsync(
        Guid caseId,
        int commentId,
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projection = await context.CommentModerationDecisionProjections
            .SingleOrDefaultAsync(
                value => value.CaseId == caseId && value.CommentId == commentId,
                cancellationToken);
        if (projection is null)
        {
            projection = new CommentModerationDecisionProjection(
                caseId,
                commentId,
                observedAtUtc);
            context.CommentModerationDecisionProjections.Add(projection);
        }

        var observation = projection.Observe(decision, observedAtUtc);
        var outcome = observation switch
        {
            CommentModerationObservation.Ignored => CommentModerationApplyOutcome.Ignored,
            CommentModerationObservation.Applied => CommentModerationApplyOutcome.Applied,
            CommentModerationObservation.GapDetected => CommentModerationApplyOutcome.GapBuffered,
            _ => throw new InvalidOperationException("Unknown moderation observation."),
        };

        if (observation == CommentModerationObservation.GapDetected)
        {
            var existingGap = await context.CommentModerationDecisionGaps
                .SingleOrDefaultAsync(value =>
                    value.CaseId == caseId &&
                    value.CommentId == commentId &&
                    value.DecisionVersion == decision.DecisionVersion,
                    cancellationToken);
            if (existingGap is not null)
            {
                if (!existingGap.Matches(decision))
                    throw new InvalidOperationException(
                        "Conflicting moderation decisions share the same case/resource version.");
                outcome = CommentModerationApplyOutcome.Ignored;
            }
            else
            {
                var gapCount = await context.CommentModerationDecisionGaps.CountAsync(
                    value => value.CaseId == caseId && value.CommentId == commentId,
                    cancellationToken);
                if (gapCount >= MaximumBufferedGapsPerCase)
                    outcome = CommentModerationApplyOutcome.GapBufferFull;
                else
                    context.CommentModerationDecisionGaps.Add(
                        new CommentModerationDecisionGap(
                            caseId,
                            commentId,
                            decision,
                            observedAtUtc));
            }
        }
        else if (observation == CommentModerationObservation.Applied)
        {
            for (var drained = 0; drained < MaximumBufferedGapsPerCase; drained++)
            {
                var next = await context.CommentModerationDecisionGaps
                    .SingleOrDefaultAsync(value =>
                        value.CaseId == caseId &&
                        value.CommentId == commentId &&
                        value.DecisionVersion == projection.AppliedVersion + 1,
                        cancellationToken);
                if (next is null)
                    break;

                projection.ApplyBuffered(next.ToSnapshot(), observedAtUtc);
                context.CommentModerationDecisionGaps.Remove(next);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        var bufferedGapCount = await context.CommentModerationDecisionGaps.CountAsync(
            value => value.CaseId == caseId && value.CommentId == commentId,
            cancellationToken);
        return new(
            outcome,
            projection.AppliedVersion,
            projection.HighestSeenVersion,
            projection.IsSuppressed,
            bufferedGapCount);
    }
}
