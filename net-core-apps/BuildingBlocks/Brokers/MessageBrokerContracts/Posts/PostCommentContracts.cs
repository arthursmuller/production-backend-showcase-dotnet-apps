namespace MessageBrokerContracts.Posts;

/// <summary>Canonical transport-neutral comment-count change for a post target.</summary>
public sealed record PostCommentCountChanged
{
    public required Guid EventId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required string PostId { get; init; }
    public required string PostType { get; init; }
    public required int ActorUserId { get; init; }
    public required int Delta { get; init; }
}
