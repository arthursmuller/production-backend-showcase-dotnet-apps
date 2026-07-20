namespace MessageBrokerContracts.Categories;

/// <summary>Canonical transport-neutral category-follow state change.</summary>
public sealed record CategoryFollowsChanged
{
    public required Guid EventId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public IReadOnlyList<int> CategoryIds { get; init; } = [];
    public IReadOnlyList<string> CategoryNames { get; init; } = [];
    public required bool IsFollowing { get; init; }
}
