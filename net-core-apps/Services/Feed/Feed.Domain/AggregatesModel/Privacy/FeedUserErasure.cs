using System;

namespace Feed.Domain.AggregatesModel;

public enum FeedUserErasureStatus : byte
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
}

public enum FeedUserErasureStage : byte
{
    PublisherFanoutWork = 0,
    PublisherRevocationWork = 1,
    PublisherEntries = 2,
    PublisherActivities = 3,
    PublisherActivityQuarantine = 4,
    PublisherState = 5,
    ReaderEntries = 9,
    ReaderState = 10,
    RevocationsAsOwner = 11,
    RevocationsAsViewer = 12,
    RelationshipQuarantineAsOwner = 13,
    RelationshipQuarantineAsViewer = 14,
    AudienceEdgesAsOwner = 15,
    AudienceEdgesAsViewer = 16,
    RelationshipCheckpointsAsOwner = 17,
    RelationshipCheckpointsAsViewer = 18,
    CacheExpiryBarrier = 19,
    Complete = 20,
}

/// <summary>
/// Permanent local privacy authority and bounded cleanup checkpoint. The row is never deleted:
/// delayed events, bootstrap pages, and projection rebuilds consult it before writing user data.
/// </summary>
public sealed class FeedUserErasure
{
    public int UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid RequestEventId { get; private set; }
    public long ErasureVersion { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public FeedUserErasureStatus Status { get; private set; }
    public FeedUserErasureStage Stage { get; private set; }
    public long Cursor { get; private set; }
    public long SecondaryCursor { get; private set; }
    public string? LeaseOwner { get; private set; }
    public long LeaseToken { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? CacheSafeAfterUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletionEventId { get; private set; }
    public long RowVersion { get; private set; }

    private FeedUserErasure() { }

    public static FeedUserErasure Create(
        int userId,
        Guid requestId,
        Guid requestEventId,
        long erasureVersion,
        DateTime requestedAtUtc,
        DateTime nowUtc)
    {
        ValidateRequest(userId, requestId, requestEventId, erasureVersion);
        var now = NormalizeUtc(nowUtc);
        return new FeedUserErasure
        {
            UserId = userId,
            RequestId = requestId,
            RequestEventId = requestEventId,
            ErasureVersion = erasureVersion,
            RequestedAtUtc = NormalizeUtc(requestedAtUtc),
            Status = FeedUserErasureStatus.Pending,
            Stage = FeedUserErasureStage.PublisherFanoutWork,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1,
        };
    }

    public void ScheduleNewer(
        Guid requestId,
        Guid requestEventId,
        long erasureVersion,
        DateTime requestedAtUtc,
        DateTime nowUtc)
    {
        ValidateRequest(UserId, requestId, requestEventId, erasureVersion);
        if (erasureVersion <= ErasureVersion)
            throw new InvalidOperationException("Only a newer erasure version can replace this tombstone.");

        RequestId = requestId;
        RequestEventId = requestEventId;
        ErasureVersion = erasureVersion;
        RequestedAtUtc = NormalizeUtc(requestedAtUtc);
        Status = FeedUserErasureStatus.Pending;
        Stage = FeedUserErasureStage.PublisherFanoutWork;
        Cursor = 0;
        SecondaryCursor = 0;
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        CacheSafeAfterUtc = null;
        CompletedAtUtc = null;
        CompletionEventId = null;
        UpdatedAtUtc = NormalizeUtc(nowUtc);
    }

    public void Claim(string owner, DateTime leaseExpiresAtUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128)
            throw new ArgumentException("A bounded lease owner is required.", nameof(owner));
        if (Status == FeedUserErasureStatus.Completed)
            throw new InvalidOperationException("A completed erasure cannot be claimed.");

        var now = NormalizeUtc(nowUtc);
        var expiry = NormalizeUtc(leaseExpiresAtUtc);
        if (expiry <= now)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc));

        LeaseOwner = owner;
        LeaseToken = checked(LeaseToken + 1);
        LeaseExpiresAtUtc = expiry;
        Status = FeedUserErasureStatus.Processing;
        AttemptCount = checked(AttemptCount + 1);
        UpdatedAtUtc = now;
    }

    public bool OwnsLease(string owner, long token, DateTime nowUtc) =>
        Status == FeedUserErasureStatus.Processing
        && string.Equals(LeaseOwner, owner, StringComparison.Ordinal)
        && LeaseToken == token
        && LeaseExpiresAtUtc > NormalizeUtc(nowUtc);

    public void Advance(
        FeedUserErasureStage stage,
        long cursor,
        long secondaryCursor,
        string owner,
        long token,
        DateTime leaseExpiresAtUtc,
        DateTime nowUtc,
        DateTime? cacheSafeAfterUtc = null)
    {
        if (!OwnsLease(owner, token, nowUtc))
            throw new InvalidOperationException("The erasure lease was lost.");
        if (stage < Stage || cursor < 0 || secondaryCursor < 0)
            throw new InvalidOperationException("Erasure progress cannot move backwards.");

        Stage = stage;
        Cursor = cursor;
        SecondaryCursor = secondaryCursor;
        LeaseExpiresAtUtc = NormalizeUtc(leaseExpiresAtUtc);
        CacheSafeAfterUtc = cacheSafeAfterUtc.HasValue
            ? NormalizeUtc(cacheSafeAfterUtc.Value)
            : CacheSafeAfterUtc;
        UpdatedAtUtc = NormalizeUtc(nowUtc);
    }

    public void Complete(Guid eventId, string owner, long token, DateTime nowUtc)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A completion event ID is required.", nameof(eventId));
        if (!OwnsLease(owner, token, nowUtc) || Stage != FeedUserErasureStage.Complete)
            throw new InvalidOperationException("Only the fenced final stage can complete erasure.");

        var now = NormalizeUtc(nowUtc);
        Status = FeedUserErasureStatus.Completed;
        Cursor = 0;
        SecondaryCursor = 0;
        CompletionEventId = eventId;
        CompletedAtUtc = now;
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        UpdatedAtUtc = now;
    }

    public void Release(string owner, long token, DateTime nowUtc)
    {
        if (!OwnsLease(owner, token, nowUtc))
            return;

        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        Status = FeedUserErasureStatus.Pending;
        UpdatedAtUtc = NormalizeUtc(nowUtc);
    }

    private static void ValidateRequest(
        int userId,
        Guid requestId,
        Guid requestEventId,
        long erasureVersion)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("A request ID is required.", nameof(requestId));
        if (requestEventId == Guid.Empty)
            throw new ArgumentException("An event ID is required.", nameof(requestEventId));
        if (erasureVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(erasureVersion));
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        if (value == default)
            throw new ArgumentException("A timestamp is required.", nameof(value));
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}

public sealed class FeedUserMutationGuard
{
    public int UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private FeedUserMutationGuard() { }
}
