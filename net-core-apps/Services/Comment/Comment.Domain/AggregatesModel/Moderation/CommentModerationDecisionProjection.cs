using System;

namespace Comment.Domain.AggregatesModel.Moderation;

public enum CommentModerationAction : byte
{
    NoAction = 1,
    LimitDistribution = 2,
    RemoveContent = 3,
    SuspendAccount = 4,
    PreserveForReview = 5,
}

public sealed record CommentModerationDecisionSnapshot(
    CommentModerationAction Action,
    long DecisionVersion,
    DateTimeOffset OccurredAtUtc);

public enum CommentModerationObservation
{
    Ignored = 0,
    Applied = 1,
    GapDetected = 2,
}

/// <summary>
/// Comment-owned projection of one moderation case/resource decision stream.
/// A version gap is itself suppressing, so an incomplete stream can never make
/// content visible. The aggregate contains no reporter, evidence, or rationale.
/// </summary>
public sealed class CommentModerationDecisionProjection
{
    private CommentModerationDecisionProjection() { }

    public CommentModerationDecisionProjection(
        Guid caseId,
        int commentId,
        DateTimeOffset observedAtUtc)
    {
        if (caseId == Guid.Empty || commentId <= 0)
            throw new InvalidOperationException("Moderation projection coordinates are invalid.");
        EnsureUtc(observedAtUtc, nameof(observedAtUtc));

        CaseId = caseId;
        CommentId = commentId;
        Action = CommentModerationAction.NoAction;
        UpdatedAtUtc = observedAtUtc;
    }

    public Guid CaseId { get; private set; }
    public int CommentId { get; private set; }
    public CommentModerationAction Action { get; private set; }
    public long AppliedVersion { get; private set; }
    public long HighestSeenVersion { get; private set; }
    public bool HasGap { get; private set; }
    public bool IsSuppressed { get; private set; }
    public DateTimeOffset? DecisionOccurredAtUtc { get; private set; }
    public DateTimeOffset? GapDetectedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public CommentModerationObservation Observe(
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc)
    {
        Validate(decision);
        EnsureUtc(observedAtUtc, nameof(observedAtUtc));

        if (decision.DecisionVersion <= AppliedVersion)
            return CommentModerationObservation.Ignored;

        HighestSeenVersion = Math.Max(HighestSeenVersion, decision.DecisionVersion);
        UpdatedAtUtc = observedAtUtc;
        if (decision.DecisionVersion != AppliedVersion + 1)
        {
            HasGap = true;
            IsSuppressed = true;
            GapDetectedAtUtc ??= observedAtUtc;
            return CommentModerationObservation.GapDetected;
        }

        ApplyNext(decision, observedAtUtc);
        return CommentModerationObservation.Applied;
    }

    public void ApplyBuffered(
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc)
    {
        Validate(decision);
        EnsureUtc(observedAtUtc, nameof(observedAtUtc));
        if (decision.DecisionVersion != AppliedVersion + 1)
            throw new InvalidOperationException("Only the next moderation decision may be drained.");

        HighestSeenVersion = Math.Max(HighestSeenVersion, decision.DecisionVersion);
        ApplyNext(decision, observedAtUtc);
    }

    private void ApplyNext(
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc)
    {
        Action = decision.Action;
        AppliedVersion = decision.DecisionVersion;
        DecisionOccurredAtUtc = decision.OccurredAtUtc;
        HasGap = AppliedVersion < HighestSeenVersion;
        IsSuppressed = HasGap || Action != CommentModerationAction.NoAction;
        GapDetectedAtUtc = HasGap ? GapDetectedAtUtc ?? observedAtUtc : null;
        UpdatedAtUtc = observedAtUtc;
    }

    private static void Validate(CommentModerationDecisionSnapshot decision)
    {
        if (decision is null || decision.DecisionVersion <= 0 ||
            !Enum.IsDefined(decision.Action))
            throw new InvalidOperationException("Moderation decision coordinates are invalid.");
        EnsureUtc(decision.OccurredAtUtc, nameof(decision.OccurredAtUtc));
    }

    private static void EnsureUtc(DateTimeOffset value, string field)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw new InvalidOperationException($"{field} must be a non-default UTC timestamp.");
    }
}

/// <summary>A bounded, content-free buffer for one out-of-order public decision.</summary>
public sealed class CommentModerationDecisionGap
{
    private CommentModerationDecisionGap() { }

    public CommentModerationDecisionGap(
        Guid caseId,
        int commentId,
        CommentModerationDecisionSnapshot decision,
        DateTimeOffset observedAtUtc)
    {
        if (caseId == Guid.Empty || commentId <= 0 || decision is null ||
            decision.DecisionVersion <= 0 || !Enum.IsDefined(decision.Action) ||
            decision.OccurredAtUtc == default || decision.OccurredAtUtc.Offset != TimeSpan.Zero ||
            observedAtUtc == default || observedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Moderation gap coordinates are invalid.");

        CaseId = caseId;
        CommentId = commentId;
        DecisionVersion = decision.DecisionVersion;
        Action = decision.Action;
        OccurredAtUtc = decision.OccurredAtUtc;
        ObservedAtUtc = observedAtUtc;
    }

    public Guid CaseId { get; private set; }
    public int CommentId { get; private set; }
    public long DecisionVersion { get; private set; }
    public CommentModerationAction Action { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }

    public CommentModerationDecisionSnapshot ToSnapshot() =>
        new(Action, DecisionVersion, OccurredAtUtc);

    public bool Matches(CommentModerationDecisionSnapshot decision) =>
        decision is not null &&
        DecisionVersion == decision.DecisionVersion &&
        Action == decision.Action &&
        OccurredAtUtc == decision.OccurredAtUtc;
}
