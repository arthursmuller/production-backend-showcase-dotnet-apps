using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

/// <summary>
/// Restricted authoritative decision state. This aggregate child intentionally contains only
/// the fields permitted to leave the Moderation boundary during projection rebuilds.
/// </summary>
public sealed class ModerationDecisionState
{
    private ModerationDecisionState() { }

    private ModerationDecisionState(
        Guid caseId,
        ResourceReference resource,
        DecisionAction action,
        long decisionVersion,
        long caseVersion,
        DateTimeOffset decidedAtUtc)
    {
        CaseId = caseId;
        ResourceKind = resource.Kind;
        ResourceId = resource.ResourceId;
        Apply(action, decisionVersion, caseVersion, decidedAtUtc, isInitial: true);
    }

    public long ExportCursor { get; private set; }
    public Guid CaseId { get; private set; }
    public ResourceKind ResourceKind { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public DecisionAction Action { get; private set; }
    public long DecisionVersion { get; private set; }
    public long CaseVersion { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }

    public static ModerationDecisionState Create(
        Guid caseId,
        ResourceReference resource,
        DecisionAction action,
        long decisionVersion,
        long caseVersion,
        DateTimeOffset decidedAtUtc)
    {
        if (caseId == Guid.Empty)
            throw new ModerationValidationException("A stable case identifier is required.");
        return new ModerationDecisionState(
            caseId,
            resource,
            action,
            decisionVersion,
            caseVersion,
            decidedAtUtc);
    }

    public void Apply(
        DecisionAction action,
        long decisionVersion,
        long caseVersion,
        DateTimeOffset decidedAtUtc) =>
        Apply(action, decisionVersion, caseVersion, decidedAtUtc, isInitial: false);

    private void Apply(
        DecisionAction action,
        long decisionVersion,
        long caseVersion,
        DateTimeOffset decidedAtUtc,
        bool isInitial)
    {
        if (!Enum.IsDefined(action))
            throw new ModerationValidationException("The decision action is not allow-listed.");
        var expected = isInitial ? 1 : checked(DecisionVersion + 1);
        if (decisionVersion != expected || caseVersion < decisionVersion)
            throw new ModerationValidationException("Decision authority versions must be monotonic.");

        Action = action;
        DecisionVersion = decisionVersion;
        CaseVersion = caseVersion;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
    }
}
