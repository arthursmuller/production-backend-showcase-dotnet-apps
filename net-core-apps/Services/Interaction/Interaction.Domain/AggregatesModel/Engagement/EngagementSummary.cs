using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

/// <summary>
/// Aggregate root for the counters exposed by the Interaction bounded context.
/// Counter changes are applied only through invariant-preserving behavior.
/// </summary>
public sealed class EngagementSummary
{
    private EngagementSummary() { }

    public EngagementSummary(ResourceRef resource, DateTimeOffset now)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        UpdatedAtUtc = now;
        LastReconciledAtUtc = DateTimeOffset.UnixEpoch;
    }

    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public long LikeCount { get; private set; }
    public long DislikeCount { get; private set; }
    public long RatingCount { get; private set; }
    public long RatingTotal { get; private set; }
    public long ViewCount { get; private set; }
    public long CommentCount { get; private set; }
    public long ShareCount { get; private set; }
    public long ContributionSourceVersion { get; private set; }
    public long ContributionHighestSeenVersion { get; private set; }
    public bool ContributionHasGap { get; private set; }
    public bool HasExternalBaseline { get; private set; }
    public bool IsErased { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset LastReconciledAtUtc { get; private set; }

    public void ApplyReaction(ReactionKind? previous, ReactionKind? current, DateTimeOffset now)
    {
        if (previous == current)
            return;
        ApplyReactionDelta(previous, -1);
        ApplyReactionDelta(current, 1);
        Touch(now);
    }

    public void ApplyRating(byte? previous, byte? current, DateTimeOffset now)
    {
        if (previous == current)
            return;
        if (previous.HasValue)
        { RatingCount--; RatingTotal -= previous.Value; }
        if (current.HasValue)
        { RatingCount++; RatingTotal += current.Value; }
        EnsureNonnegative();
        Touch(now);
    }

    public void ApplyViews(long count, DateTimeOffset now)
    {
        if (count <= 0)
            return;
        checked
        { ViewCount += count; }
        Touch(now);
    }

    public bool ApplyContribution(long comments, long shares, long sourceVersion, DateTimeOffset now)
    {
        if (comments < 0 || shares < 0)
            throw new InteractionValidationException("Contribution counters cannot be negative.");
        if (sourceVersion <= ContributionSourceVersion)
            return false;
        ContributionHighestSeenVersion = Math.Max(ContributionHighestSeenVersion, sourceVersion);
        if (sourceVersion != ContributionSourceVersion + 1)
        {
            ContributionHasGap = true;
            Touch(now);
            return false;
        }
        CommentCount = comments;
        ShareCount = shares;
        ContributionSourceVersion = sourceVersion;
        ContributionHasGap = ContributionSourceVersion < ContributionHighestSeenVersion;
        Touch(now);
        return true;
    }

    public bool Reconcile(long likes, long dislikes, long ratingCount, long ratingTotal, DateTimeOffset now)
    {
        if (likes < 0 || dislikes < 0 || ratingCount < 0 || ratingTotal < 0)
            throw new InteractionValidationException("Reconciled counters cannot be negative.");
        LastReconciledAtUtc = now;
        if (HasExternalBaseline)
        {
            if (LikeCount == likes && DislikeCount == dislikes &&
                RatingCount == ratingCount && RatingTotal == ratingTotal)
                HasExternalBaseline = false;
            return false;
        }
        if (LikeCount == likes && DislikeCount == dislikes && RatingCount == ratingCount && RatingTotal == ratingTotal)
            return false;
        LikeCount = likes;
        DislikeCount = dislikes;
        RatingCount = ratingCount;
        RatingTotal = ratingTotal;
        Touch(now);
        return true;
    }

    public bool EraseForDeletedResource(DateTimeOffset now)
    {
        if (IsErased
            && LikeCount == 0
            && DislikeCount == 0
            && RatingCount == 0
            && RatingTotal == 0
            && ViewCount == 0
            && CommentCount == 0
            && ShareCount == 0)
        {
            return false;
        }

        LikeCount = 0;
        DislikeCount = 0;
        RatingCount = 0;
        RatingTotal = 0;
        ViewCount = 0;
        CommentCount = 0;
        ShareCount = 0;
        ContributionHasGap = false;
        HasExternalBaseline = false;
        IsErased = true;
        Version++;
        UpdatedAtUtc = now;
        return true;
    }

    private void ApplyReactionDelta(ReactionKind? kind, long delta)
    {
        if (kind == ReactionKind.Like)
            LikeCount += delta;
        if (kind == ReactionKind.Dislike)
            DislikeCount += delta;
        EnsureNonnegative();
    }

    private void Touch(DateTimeOffset now)
    {
        EnsureNonnegative();
        IsErased = false;
        Version++;
        UpdatedAtUtc = now;
    }

    private void EnsureNonnegative()
    {
        if (LikeCount < 0 || DislikeCount < 0 || RatingCount < 0 || RatingTotal < 0 ||
            ViewCount < 0 || CommentCount < 0 || ShareCount < 0)
            throw new InteractionConflictException("Engagement counters cannot become negative.");
    }
}
