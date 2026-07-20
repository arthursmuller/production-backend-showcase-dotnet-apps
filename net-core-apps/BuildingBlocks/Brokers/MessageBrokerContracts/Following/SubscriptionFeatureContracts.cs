namespace MessageBrokerContracts.Following;

/// <summary>
/// Current networking-subscription update consumed by Following. The contract uses explicit
/// domain names and has no serializer-specific envelope aliases.
/// </summary>
public sealed record SubscriptionFeatureUpdated
{
    public required Guid EventId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required SubscriptionFeatureParticipant Owner { get; init; }
    public required SubscriptionFeatureParticipant Requester { get; init; }
    public required string SubscriptionId { get; init; }
    public DateTime? EndDateUtc { get; init; }
}

public sealed record SubscriptionFeatureParticipant
{
    public required int UserId { get; init; }
    public required string DisplayName { get; init; }
}
