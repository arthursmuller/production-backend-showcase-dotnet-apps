using Comment.Domain.Exceptions;
using Comment.Domain.AggregatesModel;
using System;

namespace Comment.Domain.AggregatesModel.Privacy;

public enum CommentUserErasureStatus : byte
{
    Pending = 1,
    Processing = 2,
    Blocked = 3,
    Completed = 4,
}

public enum CommentUserErasureStage : byte
{
    MediaDiscovery = 1,
    OwnedComments = 2,
    Mentions = 3,
    RelationshipEdges = 4,
    RelationshipCheckpoints = 5,
    RelationshipQuarantine = 6,
    ActorProjections = 7,
    MediaCleanup = 8,
    MediaRows = 9,
    Complete = 10,
}

/// <summary>
/// Permanent privacy authority and bounded-work checkpoint. This row is never
/// removed: delayed API calls, events, restores, and stream replays must continue to
/// observe the subject as erased after physical data deletion completes.
/// </summary>
public sealed class CommentUserErasure : ICommentConcurrencyTracked
{
    public int UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid RequestEventId { get; private set; }
    public long ErasureVersion { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public CommentUserErasureStatus Status { get; private set; }
    public CommentUserErasureStage Stage { get; private set; }
    public long Cursor { get; private set; }
    public string? LeaseOwner { get; private set; }
    public long FencingToken { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime? LastFailureAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletionEventId { get; private set; }
    public long Version { get; private set; } = 1;

    private CommentUserErasure() { }

    public static CommentUserErasure Schedule(
        int userId,
        Guid requestId,
        Guid eventId,
        long erasureVersion,
        DateTime requestedAtUtc,
        DateTime observedAtUtc)
    {
        ValidateRequest(userId, requestId, eventId, erasureVersion);
        var now = Utc(observedAtUtc);
        return new CommentUserErasure
        {
            UserId = userId,
            RequestId = requestId,
            RequestEventId = eventId,
            ErasureVersion = erasureVersion,
            RequestedAtUtc = Utc(requestedAtUtc),
            Status = CommentUserErasureStatus.Pending,
            Stage = CommentUserErasureStage.MediaDiscovery,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Supersede(
        Guid requestId,
        Guid eventId,
        long erasureVersion,
        DateTime requestedAtUtc,
        DateTime observedAtUtc)
    {
        ValidateRequest(UserId, requestId, eventId, erasureVersion);
        if (erasureVersion <= ErasureVersion)
            throw new CommentException("Only a newer erasure version may supersede a tombstone.", 409);

        RequestId = requestId;
        RequestEventId = eventId;
        ErasureVersion = erasureVersion;
        RequestedAtUtc = Utc(requestedAtUtc);
        Status = CommentUserErasureStatus.Pending;
        Stage = CommentUserErasureStage.MediaDiscovery;
        Cursor = 0;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        CompletedAtUtc = null;
        CompletionEventId = null;
        UpdatedAtUtc = Utc(observedAtUtc);
    }

    public long Claim(string owner, DateTime observedAtUtc, TimeSpan leaseDuration)
    {
        var now = Utc(observedAtUtc);
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128 || owner.IndexOfAny(['\r', '\n']) >= 0)
            throw new CommentException("A bounded erasure lease owner is required.");
        if (leaseDuration < TimeSpan.FromSeconds(10) || leaseDuration > TimeSpan.FromMinutes(5))
            throw new CommentException("The erasure lease duration is invalid.");
        if (Status is CommentUserErasureStatus.Completed or CommentUserErasureStatus.Blocked)
            throw new CommentException("The erasure is not claimable.", 409);

        LeaseOwner = owner.Trim();
        LeaseUntilUtc = now.Add(leaseDuration);
        FencingToken = checked(FencingToken + 1);
        AttemptCount = checked(AttemptCount + 1);
        Status = CommentUserErasureStatus.Processing;
        UpdatedAtUtc = now;
        return FencingToken;
    }

    public bool OwnsLease(string owner, long token, long erasureVersion, DateTime observedAtUtc) =>
        Status == CommentUserErasureStatus.Processing
        && string.Equals(LeaseOwner, owner, StringComparison.Ordinal)
        && FencingToken == token
        && ErasureVersion == erasureVersion
        && LeaseUntilUtc > Utc(observedAtUtc);

    public void Advance(
        CommentUserErasureStage stage,
        long cursor,
        string owner,
        long token,
        long erasureVersion,
        DateTime observedAtUtc,
        TimeSpan leaseDuration)
    {
        var now = Utc(observedAtUtc);
        if (!OwnsLease(owner, token, erasureVersion, now))
            throw new CommentException("The erasure lease was lost.", 409);
        if (stage < Stage || cursor < 0 || (stage == Stage && cursor < Cursor))
            throw new CommentException("Erasure progress cannot move backwards.", 409);
        if (leaseDuration < TimeSpan.FromSeconds(10) || leaseDuration > TimeSpan.FromMinutes(5))
            throw new CommentException("The erasure lease duration is invalid.");

        Stage = stage;
        Cursor = cursor;
        LeaseUntilUtc = now.Add(leaseDuration);
        LastFailureCode = null;
        LastFailureAtUtc = null;
        UpdatedAtUtc = now;
    }

    public void Release(
        string owner,
        long token,
        long erasureVersion,
        DateTime observedAtUtc,
        string? failureCode = null)
    {
        var now = Utc(observedAtUtc);
        if (!OwnsLease(owner, token, erasureVersion, now))
            return;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        Status = CommentUserErasureStatus.Pending;
        LastFailureCode = NormalizeFailure(failureCode);
        LastFailureAtUtc = LastFailureCode is null ? null : now;
        UpdatedAtUtc = now;
    }

    public void Block(
        string failureCode,
        string owner,
        long token,
        long erasureVersion,
        DateTime observedAtUtc)
    {
        var now = Utc(observedAtUtc);
        if (!OwnsLease(owner, token, erasureVersion, now))
            throw new CommentException("The erasure lease was lost.", 409);
        LastFailureCode = NormalizeFailure(failureCode) ?? "blocked";
        LastFailureAtUtc = now;
        Status = CommentUserErasureStatus.Blocked;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        UpdatedAtUtc = now;
    }

    public void Complete(
        Guid completionEventId,
        string owner,
        long token,
        long erasureVersion,
        DateTime observedAtUtc)
    {
        var now = Utc(observedAtUtc);
        if (completionEventId == Guid.Empty)
            throw new CommentException("A completion event id is required.");
        if (!OwnsLease(owner, token, erasureVersion, now) || Stage != CommentUserErasureStage.Complete)
            throw new CommentException("Only the fenced final erasure stage can complete.", 409);

        CompletionEventId = completionEventId;
        CompletedAtUtc = now;
        Status = CommentUserErasureStatus.Completed;
        Cursor = 0;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        UpdatedAtUtc = now;
    }

    private static void ValidateRequest(int userId, Guid requestId, Guid eventId, long version)
    {
        if (userId <= 0 || requestId == Guid.Empty || eventId == Guid.Empty || version <= 0)
            throw new CommentException("The user erasure request is invalid.");
    }

    private static string? NormalizeFailure(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > 64)
            normalized = normalized[..64];
        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
                return "unknown";
        }
        return normalized;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ when value == default => throw new CommentException("A timestamp is required."),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

public sealed class CommentErasureMediaObject : ICommentConcurrencyTracked
{
    public long Id { get; private set; }
    public int UserId { get; private set; }
    public int IngestionId { get; private set; }
    public string ObjectKind { get; private set; } = string.Empty;
    public string ObjectName { get; private set; } = string.Empty;
    public string ObjectVersion { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? LastFailureCode { get; private set; }
    public long Version { get; private set; } = 1;

    private CommentErasureMediaObject() { }

    public static CommentErasureMediaObject Create(
        int userId,
        int ingestionId,
        string objectKind,
        string objectName,
        string? objectVersion,
        DateTime observedAtUtc)
    {
        if (userId <= 0 || ingestionId <= 0)
            throw new CommentException("Erasure media ownership is invalid.");
        var kind = objectKind?.Trim().ToLowerInvariant();
        if (kind is not ("final" or "quarantine"))
            throw new CommentException("Erasure media kind is invalid.");
        if (string.IsNullOrWhiteSpace(objectName) || objectName.Length > 255)
            throw new CommentException("Erasure media object name is invalid.");
        return new CommentErasureMediaObject
        {
            UserId = userId,
            IngestionId = ingestionId,
            ObjectKind = kind,
            ObjectName = objectName.Trim(),
            ObjectVersion = string.IsNullOrWhiteSpace(objectVersion)
                ? "not-reported"
                : objectVersion.Trim()[..Math.Min(objectVersion.Trim().Length, 256)],
            CreatedAtUtc = Utc(observedAtUtc),
        };
    }

    public void RecordFailure(string failureCode, DateTime observedAtUtc)
    {
        Attempts = checked(Attempts + 1);
        LastAttemptAtUtc = Utc(observedAtUtc);
        LastFailureCode = string.Equals(
            failureCode?.Trim(), "delete_failed", StringComparison.OrdinalIgnoreCase)
            ? "delete_failed"
            : "unknown";
    }

    public void MarkDeleted(DateTime observedAtUtc)
    {
        if (DeletedAtUtc.HasValue)
            return;
        Attempts = checked(Attempts + 1);
        LastAttemptAtUtc = Utc(observedAtUtc);
        DeletedAtUtc = LastAttemptAtUtc;
        LastFailureCode = null;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ when value == default => throw new CommentException("A timestamp is required."),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

public sealed class CommentUserMutationGuard
{
    public int UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private CommentUserMutationGuard() { }
}
