using System;

namespace Comment.Domain.AggregatesModel.Interactions;

public enum CommentReactionKind : byte
{
    Like = 1,
    Dislike = 2,
}

/// <summary>
/// Comment-local actor projection with independent monotonic reaction and
/// rating channels.
/// </summary>
public sealed class CommentInteractionActorProjection
{
    private CommentInteractionActorProjection() { }

    public CommentInteractionActorProjection(int commentId, long actorId)
    {
        if (commentId <= 0 || actorId <= 0)
            throw new InvalidOperationException(
                "Interaction actor projection identifiers must be positive.");

        CommentId = commentId;
        ActorId = actorId;
    }

    public int CommentId { get; private set; }
    public long ActorId { get; private set; }
    public CommentReactionKind? Reaction { get; private set; }
    public byte? Rating { get; private set; }
    public long ReactionVersion { get; private set; }
    public long RatingVersion { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool ApplyReaction(
        CommentReactionKind? reaction,
        long version,
        DateTimeOffset occurredAtUtc)
    {
        if ((reaction.HasValue && !Enum.IsDefined(reaction.Value)) || version <= 0)
            throw new InvalidOperationException("Interaction reaction state is invalid.");
        if (version <= ReactionVersion)
            return false;

        Reaction = reaction;
        ReactionVersion = version;
        UpdatedAtUtc = occurredAtUtc;
        return true;
    }

    public bool ApplyRating(byte? score, long version, DateTimeOffset occurredAtUtc)
    {
        if ((score.HasValue && score.Value is < 1 or > 6) || version <= 0)
            throw new InvalidOperationException("Interaction rating state is invalid.");
        if (version <= RatingVersion)
            return false;

        Rating = score;
        RatingVersion = version;
        UpdatedAtUtc = occurredAtUtc;
        return true;
    }
}
