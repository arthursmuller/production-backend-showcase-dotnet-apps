namespace MessageBrokerContracts.Categories;

/// <summary>Canonical transport-neutral category-mention delta.</summary>
public sealed record CategoryMentionsChanged
{
    public required Guid EventId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public IReadOnlyList<string> NamesAdded { get; init; } = [];
    public IReadOnlyList<string> NamesRemoved { get; init; } = [];
}
