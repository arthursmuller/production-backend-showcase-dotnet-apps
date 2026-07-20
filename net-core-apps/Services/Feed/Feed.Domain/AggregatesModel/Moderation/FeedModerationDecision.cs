using System;
using System.Linq;

namespace Feed.Domain.AggregatesModel.Moderation;

public static class FeedModerationResourceKinds
{
    public const string Post = "post";
    public const string Comment = "comment";
    public const string Profile = "profile";
    public const string Message = "message";
    public const string Media = "media";
    public const int MaximumResourceIdLength = 128;

    public static string Normalize(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Post or Comment or Profile or Message or Media
            ? normalized
            : throw new ArgumentException("Unsupported moderated resource kind.", nameof(value));
    }

    public static string NormalizeId(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("A moderated resource ID is required.", nameof(value));
        if (normalized.Length > MaximumResourceIdLength)
        {
            throw new ArgumentException(
                $"A moderated resource ID cannot exceed {MaximumResourceIdLength} characters.",
                nameof(value));
        }
        if (!char.IsAsciiLetterOrDigit(normalized[0])
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ':' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "A moderated resource ID must be an opaque ASCII identifier.",
                nameof(value));
        }
        return normalized;
    }
}

public static class FeedModerationActions
{
    public const string NoAction = "no_action";
    public const string LimitDistribution = "limit_distribution";
    public const string RemoveContent = "remove_content";
    public const string SuspendAccount = "suspend_account";
    public const string PreserveForReview = "preserve_for_review";

    public static string Normalize(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is NoAction or LimitDistribution or RemoveContent
            or SuspendAccount or PreserveForReview
            ? normalized
            : throw new ArgumentException("Unsupported moderation action.", nameof(value));
    }
}

public static class FeedModerationProjectionStates
{
    public const string Healthy = "healthy";
    public const string Gap = "gap";
}

public enum FeedModerationVersionDisposition
{
    Applied,
    StaleOrDuplicate,
    Gap,
}

public sealed record FeedModerationDecisionInput(
    Guid CaseId,
    string ResourceKind,
    string ResourceId,
    string Action,
    long DecisionVersion,
    DateTime OccurredAtUtc)
{
    public static FeedModerationDecisionInput Create(
        Guid caseId,
        string resourceKind,
        string resourceId,
        string action,
        long decisionVersion,
        DateTime occurredAtUtc)
    {
        if (caseId == Guid.Empty)
            throw new ArgumentException("A moderation case ID is required.", nameof(caseId));
        if (decisionVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(decisionVersion));
        if (occurredAtUtc == default)
            throw new ArgumentException("A moderation occurrence timestamp is required.", nameof(occurredAtUtc));
        var occurred = occurredAtUtc.Kind switch
        {
            DateTimeKind.Utc => occurredAtUtc,
            DateTimeKind.Local => occurredAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
        };
        return new FeedModerationDecisionInput(
            caseId,
            FeedModerationResourceKinds.Normalize(resourceKind),
            FeedModerationResourceKinds.NormalizeId(resourceId),
            FeedModerationActions.Normalize(action),
            decisionVersion,
            occurred);
    }
}

/// <summary>
/// Feed-owned, evidence-free projection of a Moderation case/resource decision stream.
/// A missing version always suppresses the resource until contiguous recovery completes.
/// </summary>
public sealed class FeedModerationDecisionState
{
    public Guid CaseId { get; private set; }
    public string ResourceKind { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public long LastAppliedVersion { get; private set; }
    public long HighestObservedVersion { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool IsSuppressed { get; private set; }
    public bool NeedsReconciliation { get; private set; }
    public DateTime? LastSourceOccurredAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    private FeedModerationDecisionState() { }

    public static FeedModerationDecisionState Create(
        FeedModerationDecisionInput input,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalized = FeedModerationDecisionInput.Create(
            input.CaseId,
            input.ResourceKind,
            input.ResourceId,
            input.Action,
            input.DecisionVersion,
            input.OccurredAtUtc);
        var observed = NormalizeUtc(observedAtUtc, nameof(observedAtUtc));
        var isFirst = normalized.DecisionVersion == 1;
        return new FeedModerationDecisionState
        {
            CaseId = normalized.CaseId,
            ResourceKind = normalized.ResourceKind,
            ResourceId = normalized.ResourceId,
            LastAppliedVersion = isFirst ? 1 : 0,
            HighestObservedVersion = normalized.DecisionVersion,
            Action = isFirst ? normalized.Action : FeedModerationActions.NoAction,
            Status = isFirst
                ? FeedModerationProjectionStates.Healthy
                : FeedModerationProjectionStates.Gap,
            IsSuppressed = !isFirst || normalized.Action != FeedModerationActions.NoAction,
            LastSourceOccurredAtUtc = isFirst ? normalized.OccurredAtUtc : null,
            CreatedAtUtc = observed,
            UpdatedAtUtc = observed,
            Version = 1,
        };
    }

    public FeedModerationVersionDisposition Observe(
        FeedModerationDecisionInput input,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureCoordinate(input);
        var observed = NormalizeUtc(observedAtUtc, nameof(observedAtUtc));
        if (input.DecisionVersion <= LastAppliedVersion)
            return FeedModerationVersionDisposition.StaleOrDuplicate;

        HighestObservedVersion = Math.Max(HighestObservedVersion, input.DecisionVersion);
        if (input.DecisionVersion != LastAppliedVersion + 1)
        {
            Status = FeedModerationProjectionStates.Gap;
            IsSuppressed = true;
            UpdatedAtUtc = observed;
            Version++;
            return FeedModerationVersionDisposition.Gap;
        }

        LastAppliedVersion = input.DecisionVersion;
        Action = FeedModerationActions.Normalize(input.Action);
        LastSourceOccurredAtUtc = NormalizeUtc(input.OccurredAtUtc, nameof(input.OccurredAtUtc));
        Status = LastAppliedVersion == HighestObservedVersion
            ? FeedModerationProjectionStates.Healthy
            : FeedModerationProjectionStates.Gap;
        IsSuppressed = Status != FeedModerationProjectionStates.Healthy
            || Action != FeedModerationActions.NoAction;
        UpdatedAtUtc = observed;
        Version++;
        return FeedModerationVersionDisposition.Applied;
    }

    public void RequireReconciliation(DateTime observedAtUtc)
    {
        NeedsReconciliation = true;
        IsSuppressed = true;
        UpdatedAtUtc = NormalizeUtc(observedAtUtc, nameof(observedAtUtc));
        Version++;
    }

    public void CompleteReconciliation(DateTime observedAtUtc)
    {
        NeedsReconciliation = false;
        IsSuppressed = Status != FeedModerationProjectionStates.Healthy
            || Action != FeedModerationActions.NoAction;
        UpdatedAtUtc = NormalizeUtc(observedAtUtc, nameof(observedAtUtc));
        Version++;
    }

    private void EnsureCoordinate(FeedModerationDecisionInput input)
    {
        if (input.CaseId != CaseId
            || !string.Equals(
                FeedModerationResourceKinds.Normalize(input.ResourceKind),
                ResourceKind,
                StringComparison.Ordinal)
            || !string.Equals(
                FeedModerationResourceKinds.NormalizeId(input.ResourceId),
                ResourceId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Moderation stream coordinates are immutable.");
        }
    }

    private static DateTime NormalizeUtc(DateTime value, string parameterName)
    {
        if (value == default)
            throw new ArgumentException("A timestamp is required.", parameterName);
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}

public sealed class FeedModerationDecisionGap
{
    public Guid CaseId { get; private set; }
    public string ResourceKind { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public long DecisionVersion { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public DateTime SourceOccurredAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    private FeedModerationDecisionGap() { }

    public static FeedModerationDecisionGap Create(
        FeedModerationDecisionInput input,
        DateTime observedAtUtc)
    {
        var normalized = FeedModerationDecisionInput.Create(
            input.CaseId,
            input.ResourceKind,
            input.ResourceId,
            input.Action,
            input.DecisionVersion,
            input.OccurredAtUtc);
        var observed = observedAtUtc.Kind switch
        {
            DateTimeKind.Utc => observedAtUtc,
            DateTimeKind.Local => observedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc),
        };
        return new FeedModerationDecisionGap
        {
            CaseId = normalized.CaseId,
            ResourceKind = normalized.ResourceKind,
            ResourceId = normalized.ResourceId,
            DecisionVersion = normalized.DecisionVersion,
            Action = normalized.Action,
            SourceOccurredAtUtc = normalized.OccurredAtUtc,
            RecordedAtUtc = observed,
        };
    }

    public FeedModerationDecisionInput ToInput() => FeedModerationDecisionInput.Create(
        CaseId,
        ResourceKind,
        ResourceId,
        Action,
        DecisionVersion,
        SourceOccurredAtUtc);
}
