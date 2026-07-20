using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public sealed class ModerationCase
{
    private readonly List<ModerationAuditEntry> _auditEntries = [];

    private ModerationCase() { }

    private ModerationCase(
        Guid id,
        Guid reportId,
        ResourceReference resource,
        string reporterId,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset slaDueAtUtc)
    {
        Id = id;
        ReportId = reportId;
        ResourceKind = resource.Kind;
        ResourceId = resource.ResourceId;
        ReporterId = ModerationReport.RequireActor(reporterId);
        Status = CaseStatus.Received;
        Priority = CasePriority.Normal;
        Version = 1;
        ReceivedAtUtc = receivedAtUtc;
        LastChangedAtUtc = receivedAtUtc;
        SlaDueAtUtc = slaDueAtUtc;
        AppendAudit(reporterId, "case.received", "report-submitted", receivedAtUtc);
    }

    public Guid Id { get; private set; }
    public Guid ReportId { get; private set; }
    public ResourceKind ResourceKind { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public string ReporterId { get; private set; } = string.Empty;
    public CaseStatus Status { get; private set; }
    public CasePriority Priority { get; private set; }
    public string? AssignedInvestigatorId { get; private set; }
    public long Version { get; private set; }
    public long DecisionVersion { get; private set; }
    public DecisionAction? DecisionAction { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset LastChangedAtUtc { get; private set; }
    public DateTimeOffset SlaDueAtUtc { get; private set; }
    public DateTimeOffset? EscalatedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public ModerationDecisionState? DecisionState { get; private set; }
    public string LastAuditHash { get; private set; } = string.Empty;
    public DateTimeOffset? RetentionRedactedAtUtc { get; private set; }
    public long RetentionClaimToken { get; private set; }
    public string? ErasureSubjectHash { get; private set; }
    public long? ErasureSubjectUserId { get; private set; }
    public long ErasureClaimToken { get; private set; }
    public IReadOnlyCollection<ModerationAuditEntry> AuditEntries => _auditEntries;

    public bool IsTerminal => Status == CaseStatus.Closed;
    public bool HasActiveSla => Status is not (CaseStatus.Decided or CaseStatus.Closed);
    public bool CanAppeal => Status == CaseStatus.Decided;

    public static ModerationCase Create(
        Guid id,
        Guid reportId,
        ResourceReference resource,
        string reporterId,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset slaDueAtUtc)
    {
        if (id == Guid.Empty || reportId == Guid.Empty)
            throw new ModerationValidationException("Stable case and report identifiers are required.");
        if (slaDueAtUtc <= receivedAtUtc)
            throw new ModerationValidationException("The initial SLA due timestamp must be in the future.");
        return new ModerationCase(id, reportId, resource, reporterId, receivedAtUtc, slaDueAtUtc);
    }

    public static bool CanTransition(CaseStatus from, CaseStatus to) => (from, to) switch
    {
        (CaseStatus.Received, CaseStatus.Triaged) => true,
        (CaseStatus.Triaged, CaseStatus.Investigating) => true,
        (CaseStatus.Investigating, CaseStatus.DecisionPending) => true,
        (CaseStatus.DecisionPending, CaseStatus.Decided) => true,
        (CaseStatus.Decided, CaseStatus.Appealed) => true,
        (CaseStatus.Decided, CaseStatus.Closed) => true,
        (CaseStatus.Appealed, CaseStatus.Investigating) => true,
        _ => false,
    };

    public void Triage(
        string actorId,
        string reasonCode,
        CasePriority priority,
        long expectedVersion,
        DateTimeOffset now,
        DateTimeOffset nextSlaDueAtUtc)
    {
        EnsureExpectedVersion(expectedVersion);
        if (!Enum.IsDefined(priority))
            throw new ModerationValidationException("The priority is not allow-listed.");
        Priority = priority;
        Transition(CaseStatus.Triaged, actorId, "case.triaged", reasonCode, now, nextSlaDueAtUtc);
    }

    public void Assign(
        string actorId,
        string investigatorId,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now)
    {
        EnsureExpectedVersion(expectedVersion);
        if (Status is not (CaseStatus.Triaged or CaseStatus.Investigating or CaseStatus.Appealed))
            throw new ModerationConflictException("This case cannot be assigned in its current state.");
        AssignedInvestigatorId = ModerationReport.RequireActor(investigatorId);
        Touch(actorId, "case.assigned", reasonCode, now);
    }

    public void StartInvestigation(
        string actorId,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now,
        DateTimeOffset nextSlaDueAtUtc)
    {
        EnsureExpectedVersion(expectedVersion);
        if (!string.Equals(AssignedInvestigatorId, actorId, StringComparison.Ordinal))
            throw new ModerationConflictException("Only the assigned investigator can start this investigation.");
        Transition(CaseStatus.Investigating, actorId, "investigation.started", reasonCode, now, nextSlaDueAtUtc);
    }

    public void CompleteInvestigation(
        string actorId,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now,
        DateTimeOffset nextSlaDueAtUtc)
    {
        EnsureExpectedVersion(expectedVersion);
        if (!string.Equals(AssignedInvestigatorId, actorId, StringComparison.Ordinal))
            throw new ModerationConflictException("Only the assigned investigator can complete this investigation.");
        Transition(CaseStatus.DecisionPending, actorId, "investigation.completed", reasonCode, now, nextSlaDueAtUtc);
    }

    public void Decide(
        string actorId,
        string reasonCode,
        DecisionAction action,
        long expectedVersion,
        DateTimeOffset now)
    {
        EnsureExpectedVersion(expectedVersion);
        if (!Enum.IsDefined(action))
            throw new ModerationValidationException("The decision action is not allow-listed.");
        DecisionAction = action;
        DecisionVersion++;
        DecidedAtUtc = now;
        Transition(CaseStatus.Decided, actorId, "decision.made", reasonCode, now, now);
        var resource = new ResourceReference(ResourceKind, ResourceId);
        if (DecisionState is null)
        {
            DecisionState = ModerationDecisionState.Create(
                Id,
                resource,
                action,
                DecisionVersion,
                Version,
                now);
        }
        else
        {
            DecisionState.Apply(action, DecisionVersion, Version, now);
        }
    }

    public void Appeal(
        string reporterId,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now,
        DateTimeOffset nextSlaDueAtUtc)
    {
        EnsureExpectedVersion(expectedVersion);
        if (!string.Equals(ReporterId, reporterId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The report is not owned by this reporter.");
        Transition(CaseStatus.Appealed, reporterId, "decision.appealed", reasonCode, now, nextSlaDueAtUtc);
    }

    public void Close(
        string actorId,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now)
    {
        EnsureExpectedVersion(expectedVersion);
        Transition(CaseStatus.Closed, actorId, "case.closed", reasonCode, now, now);
    }

    public bool EscalateIfOverdue(string actorId, DateTimeOffset now)
    {
        if (!HasActiveSla || EscalatedAtUtc.HasValue || SlaDueAtUtc > now)
            return false;

        Priority = CasePriority.Critical;
        EscalatedAtUtc = now;
        Touch(actorId, "case.escalated", "sla-overdue", now);
        return true;
    }

    public void RecordLegalHold(
        string actorId,
        string reasonCode,
        bool placed,
        long expectedVersion,
        DateTimeOffset now)
    {
        EnsureExpectedVersion(expectedVersion);
        Touch(
            actorId,
            placed ? "legal-hold.placed" : "legal-hold.released",
            reasonCode,
            now);
    }

    public ModerationCaseMessage AddMessage(
        Guid messageId,
        string actorId,
        string reference,
        string sha256,
        string contentType,
        string reasonCode,
        long expectedVersion,
        DateTimeOffset now)
    {
        EnsureExpectedVersion(expectedVersion);
        Touch(actorId, "case.message-added", reasonCode, now);
        return ModerationCaseMessage.Create(
            messageId,
            Id,
            actorId,
            reference,
            sha256,
            contentType,
            Version,
            now);
    }

    private void Transition(
        CaseStatus target,
        string actorId,
        string action,
        string reasonCode,
        DateTimeOffset now,
        DateTimeOffset nextSlaDueAtUtc)
    {
        if (!CanTransition(Status, target))
            throw new ModerationConflictException($"Transition from {Status} to {target} is not allowed.");
        if (!IsTerminalTarget(target) && nextSlaDueAtUtc <= now)
            throw new ModerationValidationException("The next SLA due timestamp must be in the future.");

        Status = target;
        SlaDueAtUtc = nextSlaDueAtUtc;
        EscalatedAtUtc = null;
        Touch(actorId, action, reasonCode, now);
    }

    private static bool IsTerminalTarget(CaseStatus target) =>
        target is CaseStatus.Decided or CaseStatus.Closed;

    private void Touch(string actorId, string action, string reasonCode, DateTimeOffset now)
    {
        Version++;
        LastChangedAtUtc = now;
        AppendAudit(actorId, action, reasonCode, now);
    }

    private void AppendAudit(string actorId, string action, string reasonCode, DateTimeOffset now)
    {
        var audit = ModerationAuditEntry.Create(
            Id,
            actorId,
            action,
            reasonCode,
            Version,
            LastAuditHash,
            now);
        _auditEntries.Add(audit);
        LastAuditHash = audit.Hash;
    }

    private void EnsureExpectedVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
            throw new ModerationConcurrencyException(expectedVersion, Version);
    }
}
