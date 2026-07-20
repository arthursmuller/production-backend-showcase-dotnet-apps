using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

public enum InteractionUserErasureStage : byte
{
    ActorData = 1,
    AwaitingRawViewExpiry = 2,
    Completed = 3,
}

/// <summary>
/// Permanent per-subject tombstone and bounded-work checkpoint. Keeping this row after
/// completion prevents delayed messages and restores from recreating erased data.
/// </summary>
public sealed class InteractionUserErasure
{
    private InteractionUserErasure() { }

    private InteractionUserErasure(
        long userId,
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset rawViewsExpireAtUtc)
    {
        UserId = RequireUser(userId);
        RequestId = RequireRequest(requestId);
        ErasureVersion = RequireVersion(erasureVersion);
        RequestedAtUtc = requestedAtUtc;
        RawViewsExpireAtUtc = rawViewsExpireAtUtc;
        Stage = InteractionUserErasureStage.ActorData;
    }

    public long UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public long ErasureVersion { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset RawViewsExpireAtUtc { get; private set; }
    public InteractionUserErasureStage Stage { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseUntilUtc { get; private set; }
    public long FencingToken { get; private set; }
    public DateTimeOffset? LastProcessedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static InteractionUserErasure Schedule(
        long userId,
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset rawViewsExpireAtUtc)
    {
        if (rawViewsExpireAtUtc < requestedAtUtc)
            throw new InteractionValidationException("Raw-view expiry cannot precede erasure receipt.");
        return new InteractionUserErasure(
            userId, requestId, erasureVersion, requestedAtUtc, rawViewsExpireAtUtc);
    }

    public void Supersede(
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset rawViewsExpireAtUtc)
    {
        if (erasureVersion <= ErasureVersion)
            throw new InteractionConflictException("Only a newer erasure version can supersede a tombstone.");
        RequestId = RequireRequest(requestId);
        ErasureVersion = RequireVersion(erasureVersion);
        RequestedAtUtc = requestedAtUtc;
        RawViewsExpireAtUtc = rawViewsExpireAtUtc;
        Stage = InteractionUserErasureStage.ActorData;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        LastProcessedAtUtc = null;
        CompletedAtUtc = null;
    }

    public long Acquire(string owner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 64)
            throw new InteractionValidationException("A bounded erasure lease owner is required.");
        if (leaseDuration < TimeSpan.FromSeconds(30) || leaseDuration > TimeSpan.FromHours(1))
            throw new InteractionValidationException("The erasure lease duration is invalid.");
        LeaseOwner = owner;
        LeaseUntilUtc = now.Add(leaseDuration);
        FencingToken++;
        LastProcessedAtUtc = now;
        return FencingToken;
    }

    public void AwaitRawViewExpiry(DateTimeOffset now)
    {
        Stage = InteractionUserErasureStage.AwaitingRawViewExpiry;
        LastProcessedAtUtc = now;
    }

    public void Release(DateTimeOffset now)
    {
        LastProcessedAtUtc = now;
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    public void Complete(DateTimeOffset now)
    {
        if (now < RawViewsExpireAtUtc)
            throw new InteractionConflictException("Raw-view retention has not elapsed.");
        Stage = InteractionUserErasureStage.Completed;
        CompletedAtUtc = now;
        Release(now);
    }

    private static long RequireUser(long value) => value > 0
        ? value
        : throw new InteractionValidationException("A positive erasure subject is required.");

    private static Guid RequireRequest(Guid value) => value != Guid.Empty
        ? value
        : throw new InteractionValidationException("An erasure request ID is required.");

    private static long RequireVersion(long value) => value > 0
        ? value
        : throw new InteractionValidationException("A positive erasure version is required.");
}
