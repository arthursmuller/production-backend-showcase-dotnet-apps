namespace MessageBrokerContracts.Following;

/// <summary>Stable wire values for Following-owned audience relationships.</summary>
public static class RelationshipKinds
{
    public const string Follow = "follow";
    public const string Friend = "friend";
    public const string Block = "block";

    public static bool IsSupported(string? value) =>
        value is Follow or Friend or Block;
}

/// <summary>Stable wire values for active and privacy-revoking relationship states.</summary>
public static class RelationshipStates
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Blocked = "blocked";

    public static bool IsSupported(string? value) =>
        value is Active or Revoked or Blocked;
}

/// <summary>
/// Authoritative, directional relationship transition emitted by Following. StreamVersion is
/// durable per StreamId and increases across delete/recreate cycles.
/// </summary>
public sealed record RelationshipStateChanged
{
    public required Guid EventId { get; init; }
    public required Guid StreamId { get; init; }
    public required long StreamVersion { get; init; }
    public required int OwnerUserId { get; init; }
    public required int ViewerUserId { get; init; }
    public required string RelationshipKind { get; init; }
    public required string State { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required string ReasonCode { get; init; }
}

/// <summary>Current authoritative stream state used only by bounded bootstrap/reconciliation.</summary>
public sealed record RelationshipStateSnapshot
{
    public required long Cursor { get; init; }
    public required Guid StreamId { get; init; }
    public required Guid LastEventId { get; init; }
    public required long StreamVersion { get; init; }
    public required int OwnerUserId { get; init; }
    public required int ViewerUserId { get; init; }
    public required string RelationshipKind { get; init; }
    public required string State { get; init; }
    public required DateTime AsOfUtc { get; init; }
}

public sealed record RelationshipSnapshotPage
{
    public required IReadOnlyList<RelationshipStateSnapshot> Items { get; init; }
    public required long NextCursor { get; init; }
    public required bool HasMore { get; init; }
    /// <summary>
    /// Stable upper stream-row boundary captured when a bootstrap cycle starts. Callers must
    /// send it back on later pages so a continuously changing authority cannot starve completion.
    /// A value of zero represents an empty authority snapshot.
    /// </summary>
    public long SnapshotHighWatermark { get; init; }
    /// <summary>
    /// Immutable creation timestamp of the high-watermark row (Unix epoch for an empty
    /// authority). It identifies the same bounded cycle on every page; item versions remain the
    /// source of truth when live events overlap bootstrap.
    /// </summary>
    public DateTime SnapshotAsOfUtc { get; init; }
}
