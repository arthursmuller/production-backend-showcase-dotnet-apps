using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public sealed class LegalHold
{
    private LegalHold() { }

    private LegalHold(
        Guid caseId,
        ResourceReference resource,
        string actorId,
        string reasonCode,
        DateTimeOffset placedAtUtc)
    {
        Id = Guid.NewGuid();
        CaseId = caseId;
        ResourceKind = resource.Kind;
        ResourceId = resource.ResourceId;
        PlacedByActorId = ModerationReport.RequireActor(actorId);
        ReasonCode = RequireReason(reasonCode);
        PlacedAtUtc = placedAtUtc;
        IsActive = true;
        ActiveCaseKey = caseId.ToString("D");
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public ResourceKind ResourceKind { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public string PlacedByActorId { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTimeOffset PlacedAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public string? ReleasedByActorId { get; private set; }
    public string? ReleaseReasonCode { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }
    public string? ActiveCaseKey { get; private set; }

    public static LegalHold Place(
        Guid caseId,
        ResourceReference resource,
        string actorId,
        string reasonCode,
        DateTimeOffset placedAtUtc)
    {
        if (caseId == Guid.Empty)
            throw new ModerationValidationException("A legal hold must be scoped to one case record.");
        return new LegalHold(caseId, resource, actorId, reasonCode, placedAtUtc);
    }

    public void Release(string actorId, string reasonCode, DateTimeOffset releasedAtUtc)
    {
        if (!IsActive)
            throw new ModerationConflictException("The legal hold is already released.");
        IsActive = false;
        ReleasedByActorId = ModerationReport.RequireActor(actorId);
        ReleaseReasonCode = RequireReason(reasonCode);
        ReleasedAtUtc = releasedAtUtc;
        ActiveCaseKey = null;
    }

    private static string RequireReason(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 64
            || reasonCode.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_' and not '.'))
        {
            throw new ModerationValidationException("A bounded machine-readable legal-hold reason is required.");
        }
        return reasonCode;
    }
}
