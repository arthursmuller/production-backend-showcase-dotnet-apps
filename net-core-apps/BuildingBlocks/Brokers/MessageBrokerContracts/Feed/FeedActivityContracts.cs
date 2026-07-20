namespace MessageBrokerContracts.Feed;

public static class FeedActivityStates
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Deleted = "deleted";

    public static bool IsSupported(string? value) =>
        value is Active or Revoked or Deleted;
}

public static class FeedAudiencePolicies
{
    public const string Public = "public";
    public const string Followers = "followers";
    public const string Friends = "friends";
    public const string FriendsOfFriends = "friends_of_friends";
    public const string Owner = "owner";

    public static bool IsSupported(string? value) =>
        value is Public or Followers or Friends or FriendsOfFriends or Owner;

    /// <summary>Normalizes canonical policies without widening an absent or unknown value.</summary>
    public static string NormalizeOrOwner(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Public => Public,
            Followers => Followers,
            Friends => Friends,
            FriendsOfFriends => FriendsOfFriends,
            Owner => Owner,
            _ => Owner,
        };
    }
}

/// <summary>
/// Canonical transport-neutral Feed activity stream. StreamVersion is monotonic for one
/// target/activity-kind identity; transport routing and delivery metadata stay in the envelope.
/// </summary>
public sealed record FeedActivityChanged
{
    public required Guid EventId { get; init; }
    public required Guid StreamId { get; init; }
    public required long StreamVersion { get; init; }
    public required string TargetType { get; init; }
    public required string TargetId { get; init; }
    public required string ActivityKind { get; init; }
    public required int ActorUserId { get; init; }
    public required string AudiencePolicy { get; init; }
    public required string State { get; init; }
    public required long RankKey { get; init; }
    public required DateTime PublishedAtUtc { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required string ReasonCode { get; init; }
}
