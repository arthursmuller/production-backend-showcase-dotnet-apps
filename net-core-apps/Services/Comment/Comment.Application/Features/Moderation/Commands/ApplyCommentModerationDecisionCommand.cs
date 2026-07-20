using Comment.Application.Dtos.Moderation;
using Comment.Domain.AggregatesModel.Moderation;
using Comment.Domain.Repositories;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Moderation.Commands;

public sealed class ApplyCommentModerationDecisionCommand(
    ICommentModerationDecisionRepository repository,
    TimeProvider timeProvider)
{
    private const int MaximumResourceIdLength = 128;
    private static readonly DateTimeOffset EarliestAcceptedOccurrence =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<ApplyCommentModerationDecisionResponse> ExecuteAsync(
        ApplyCommentModerationDecisionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);

        if (request.ResourceKind != CommentModeratedResourceKind.Comment)
            return new(ApplyCommentModerationDecisionOutcome.NotApplicable, false, 0, 0, 0);

        if (request.ResourceId.Length > 10 ||
            !int.TryParse(request.ResourceId, NumberStyles.None,
                CultureInfo.InvariantCulture, out var commentId) || commentId <= 0)
            throw new InvalidOperationException(
                "A Comment moderation resource id must be a positive canonical integer.");
        if (!string.Equals(
            request.ResourceId,
            commentId.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal))
            throw new InvalidOperationException(
                "A Comment moderation resource id must be a positive canonical integer.");

        var now = timeProvider.GetUtcNow();
        if (request.OccurredAtUtc > now.AddMinutes(5))
            throw new InvalidOperationException(
                "A moderation decision timestamp cannot be more than five minutes in the future.");

        var result = await repository.ApplyAsync(
            request.CaseId,
            commentId,
            new CommentModerationDecisionSnapshot(
                (CommentModerationAction)request.Action,
                request.DecisionVersion,
                request.OccurredAtUtc),
            now,
            cancellationToken);

        return new(
            result.Outcome switch
            {
                CommentModerationApplyOutcome.Ignored =>
                    ApplyCommentModerationDecisionOutcome.Ignored,
                CommentModerationApplyOutcome.Applied =>
                    ApplyCommentModerationDecisionOutcome.Applied,
                CommentModerationApplyOutcome.GapBuffered =>
                    ApplyCommentModerationDecisionOutcome.GapBuffered,
                CommentModerationApplyOutcome.GapBufferFull =>
                    ApplyCommentModerationDecisionOutcome.GapBufferFull,
                _ => throw new InvalidOperationException("Unknown moderation projection outcome."),
            },
            result.IsSuppressed,
            result.AppliedVersion,
            result.HighestSeenVersion,
            result.BufferedGapCount);
    }

    private static void Validate(ApplyCommentModerationDecisionRequest request)
    {
        if (request is null || request.CaseId == Guid.Empty ||
            !Enum.IsDefined(request.ResourceKind) ||
            string.IsNullOrWhiteSpace(request.ResourceId) ||
            request.ResourceId.Length > MaximumResourceIdLength ||
            !string.Equals(request.ResourceId, request.ResourceId.Trim(), StringComparison.Ordinal) ||
            !Enum.IsDefined(request.Action) || request.DecisionVersion <= 0 ||
            request.OccurredAtUtc < EarliestAcceptedOccurrence ||
            request.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The public moderation decision coordinate is invalid.");
    }
}
