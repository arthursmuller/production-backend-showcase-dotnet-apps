namespace MessageBrokerContracts.Notification;

/// <summary>Canonical transport-neutral request for an application notification.</summary>
public sealed record NotificationRequested
{
    public required Guid EventId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required int RecipientUserId { get; init; }
    public required int SourceUserId { get; init; }
    public required int ReplacementUserId { get; init; }
    public required string SourceEntityType { get; init; }
    public required string SourceEntityId { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<int> ChannelIds { get; init; } = [];
}
