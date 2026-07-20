namespace Moderation.Domain.AggregatesModel;

/// <summary>Immutable restricted recovery revision appended for each decision-state change.</summary>
public sealed class ModerationDecisionStateRevision
{
    private ModerationDecisionStateRevision() { }

    public long RevisionCursor { get; private set; }
    public Guid CaseId { get; private set; }
    public ResourceKind ResourceKind { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public DecisionAction Action { get; private set; }
    public long DecisionVersion { get; private set; }
    public long CaseVersion { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }
    public byte PartitionKey { get; private set; }
    public string RowSha256 { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
}
