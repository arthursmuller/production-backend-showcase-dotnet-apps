namespace MessageBrokerContracts.Privacy.Restricted;

/// <summary>
/// Restricted Privacy Operations command. The request/erasure-version pair is the
/// idempotency coordinate retained by every service tombstone so a late event or rebuild
/// cannot recreate erased personal data.
/// </summary>
public sealed record UserErasureRequested
{
    public required Guid EventId { get; init; }
    public required Guid RequestId { get; init; }
    public required long UserId { get; init; }
    public required long ErasureVersion { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
}

/// <summary>
/// Restricted service acknowledgement. It deliberately carries no deleted content,
/// counts, evidence references, reason text, or failure details.
/// </summary>
public sealed record UserErasureCompleted
{
    public required Guid EventId { get; init; }
    public required Guid RequestId { get; init; }
    public required long UserId { get; init; }
    public required long ErasureVersion { get; init; }
    public required string Service { get; init; }
    public required string Outcome { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public static class UserErasureOutcomes
{
    public const string Completed = "completed";
    public const string Superseded = "superseded";
}
