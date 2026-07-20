using System;

namespace Comment.Domain.AggregatesModel.Interactions;

public sealed record CommentInteractionSummarySnapshot(
    long LikeCount,
    long DislikeCount,
    long RatingCount,
    long RatingTotal,
    long ViewCount,
    long CommentCount,
    long ShareCount,
    long Version,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Comment-owned read projection of the authoritative Interaction aggregate.
/// It accepts transport-neutral snapshots and detects version gaps before data
/// is exposed by Comment queries.
/// </summary>
public sealed class CommentInteractionSummaryProjection
{
    private CommentInteractionSummaryProjection() { }

    public CommentInteractionSummaryProjection(
        int commentId,
        CommentInteractionSummarySnapshot snapshot)
    {
        if (commentId <= 0)
            throw new InvalidOperationException("Interaction summary comment id must be positive.");

        CommentId = commentId;
        ApplyBaseline(snapshot);
    }

    public int CommentId { get; private set; }
    public long LikeCount { get; private set; }
    public long DislikeCount { get; private set; }
    public long RatingCount { get; private set; }
    public long RatingTotal { get; private set; }
    public long ViewCount { get; private set; }
    public long CommentCount { get; private set; }
    public long ShareCount { get; private set; }
    public long AppliedVersion { get; private set; }
    public long HighestSeenVersion { get; private set; }
    public bool HasGap { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool Apply(CommentInteractionSummarySnapshot snapshot)
    {
        if (snapshot.Version <= AppliedVersion)
            return false;

        HighestSeenVersion = Math.Max(HighestSeenVersion, snapshot.Version);
        if (snapshot.Version != AppliedVersion + 1)
        {
            HasGap = true;
            UpdatedAtUtc = snapshot.OccurredAtUtc;
            return true;
        }

        ApplyValues(snapshot);
        AppliedVersion = snapshot.Version;
        HasGap = AppliedVersion < HighestSeenVersion;
        return true;
    }

    private void ApplyBaseline(CommentInteractionSummarySnapshot snapshot)
    {
        if (snapshot.Version <= 0)
            throw new InvalidOperationException("Interaction summary version must be positive.");

        ApplyValues(snapshot);
        AppliedVersion = snapshot.Version;
        HighestSeenVersion = snapshot.Version;
    }

    private void ApplyValues(CommentInteractionSummarySnapshot snapshot)
    {
        if (snapshot.LikeCount < 0 || snapshot.DislikeCount < 0 ||
            snapshot.RatingCount < 0 || snapshot.RatingTotal < 0 ||
            snapshot.ViewCount < 0 || snapshot.CommentCount < 0 ||
            snapshot.ShareCount < 0)
        {
            throw new InvalidOperationException(
                "Interaction summary counters cannot be negative.");
        }

        LikeCount = snapshot.LikeCount;
        DislikeCount = snapshot.DislikeCount;
        RatingCount = snapshot.RatingCount;
        RatingTotal = snapshot.RatingTotal;
        ViewCount = snapshot.ViewCount;
        CommentCount = snapshot.CommentCount;
        ShareCount = snapshot.ShareCount;
        UpdatedAtUtc = snapshot.OccurredAtUtc;
    }
}
