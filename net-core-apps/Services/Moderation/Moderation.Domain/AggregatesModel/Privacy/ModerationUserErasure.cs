using System.Globalization;
using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public enum ModerationUserErasureStage : byte
{
    Active = 1,
    Completed = 2,
}

/// <summary>
/// Permanent privacy tombstone plus the checkpoint for bounded, legal-hold-aware
/// cleanup. Completed rows are retained so delayed reports and rebuilds cannot
/// recreate subject-owned moderation data.
/// </summary>
public sealed class ModerationUserErasure
{
    private ModerationUserErasure() { }

    private ModerationUserErasure(
        long userId,
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset receivedAtUtc)
    {
        UserId = RequireUser(userId);
        RequestId = RequireRequest(requestId);
        ErasureVersion = RequireVersion(erasureVersion);
        RequestedAtUtc = requestedAtUtc;
        NextAttemptAtUtc = receivedAtUtc;
        Stage = ModerationUserErasureStage.Active;
    }

    public long UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public long ErasureVersion { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public ModerationUserErasureStage Stage { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseUntilUtc { get; private set; }
    public long FencingToken { get; private set; }
    public DateTimeOffset? LastProcessedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int HeldCaseCount { get; private set; }

    public string ReporterId => UserId.ToString(CultureInfo.InvariantCulture);

    public static ModerationUserErasure Schedule(
        long userId,
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset receivedAtUtc) =>
        new(userId, requestId, erasureVersion, requestedAtUtc, receivedAtUtc);

    public void Supersede(
        Guid requestId,
        long erasureVersion,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset receivedAtUtc)
    {
        if (erasureVersion <= ErasureVersion)
            throw new ModerationConflictException(
                "Only a newer erasure version may supersede a tombstone.");
        RequestId = RequireRequest(requestId);
        ErasureVersion = RequireVersion(erasureVersion);
        RequestedAtUtc = requestedAtUtc;
        Stage = ModerationUserErasureStage.Active;
        NextAttemptAtUtc = receivedAtUtc;
        LeaseOwner = null;
        LeaseUntilUtc = null;
        LastProcessedAtUtc = null;
        CompletedAtUtc = null;
        HeldCaseCount = 0;
    }

    public long Acquire(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 64)
            throw new ModerationValidationException(
                "A bounded erasure lease owner is required.");
        if (leaseDuration < TimeSpan.FromSeconds(30)
            || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ModerationValidationException(
                "The erasure lease duration is invalid.");
        }

        LeaseOwner = leaseOwner;
        LeaseUntilUtc = now.Add(leaseDuration);
        LastProcessedAtUtc = now;
        FencingToken++;
        return FencingToken;
    }

    public void Continue(DateTimeOffset now)
    {
        Stage = ModerationUserErasureStage.Active;
        NextAttemptAtUtc = now;
        Release(now);
    }

    public bool Complete(
        DateTimeOffset now,
        int heldCaseCount,
        TimeSpan legalHoldReviewInterval)
    {
        if (heldCaseCount < 0)
            throw new ModerationValidationException(
                "Held-case count cannot be negative.");
        if (legalHoldReviewInterval < TimeSpan.FromMinutes(1)
            || legalHoldReviewInterval > TimeSpan.FromDays(7))
        {
            throw new ModerationValidationException(
                "The legal-hold review interval is invalid.");
        }

        var firstCompletion = CompletedAtUtc is null;
        Stage = ModerationUserErasureStage.Completed;
        HeldCaseCount = heldCaseCount;
        CompletedAtUtc ??= now;
        NextAttemptAtUtc = heldCaseCount > 0
            ? now.Add(legalHoldReviewInterval)
            : null;
        Release(now);
        return firstCompletion;
    }

    private void Release(DateTimeOffset now)
    {
        LastProcessedAtUtc = now;
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    private static long RequireUser(long value) => value > 0
        ? value
        : throw new ModerationValidationException(
            "A positive erasure subject is required.");

    private static Guid RequireRequest(Guid value) => value != Guid.Empty
        ? value
        : throw new ModerationValidationException(
            "An erasure request ID is required.");

    private static long RequireVersion(long value) => value > 0
        ? value
        : throw new ModerationValidationException(
            "A positive erasure version is required.");
}

public static class ModerationSubject
{
    public static string Normalize(string reporterId)
    {
        var actor = ModerationReport.RequireActor(reporterId);
        return TryGetUserId(actor, out var userId)
            ? userId.ToString(CultureInfo.InvariantCulture)
            : actor;
    }

    public static bool TryGetUserId(string reporterId, out long userId) =>
        long.TryParse(
            reporterId,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out userId)
        && userId > 0;
}
