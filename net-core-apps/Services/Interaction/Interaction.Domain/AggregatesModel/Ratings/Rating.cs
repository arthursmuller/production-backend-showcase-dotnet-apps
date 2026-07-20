using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

public sealed class Rating
{
    public const int MaximumCommentLength = 2_000;
    private Rating() { }

    public Rating(ResourceRef resource, long actorId, byte score, string? comment, DateTimeOffset now)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        ActorId = actorId > 0 ? actorId : throw new InteractionValidationException("Actor id must be positive.");
        Set(score, comment, now, false);
        Version = 1;
    }

    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public long ActorId { get; private set; }
    public byte Score { get; private set; }
    public string? Comment { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool Update(byte score, string? comment, DateTimeOffset now)
    {
        var normalized = NormalizeComment(comment);
        if (Score == score && string.Equals(Comment, normalized, StringComparison.Ordinal))
            return false;
        Set(score, normalized, now, true);
        return true;
    }

    private void Set(byte score, string? comment, DateTimeOffset now, bool increment)
    {
        if (score is < 1 or > 6)
            throw new InteractionValidationException("Rating score must be between 1 and 6.");
        Score = score;
        Comment = NormalizeComment(comment);
        UpdatedAtUtc = now;
        if (increment)
            Version++;
    }

    private static string? NormalizeComment(string? comment)
    {
        var normalized = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (normalized?.Length > MaximumCommentLength)
            throw new InteractionValidationException($"Rating comment cannot exceed {MaximumCommentLength} characters.");
        return normalized;
    }
}
