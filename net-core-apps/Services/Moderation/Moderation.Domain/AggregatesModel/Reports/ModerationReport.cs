using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public sealed class ModerationReport
{
    private readonly List<EvidenceReference> _evidenceReferences = [];

    private ModerationReport() { }

    private ModerationReport(
        Guid id,
        Guid caseId,
        string reporterId,
        ResourceReference resource,
        ReportReason reason,
        string idempotencyKeyHash,
        string requestHash,
        DateTimeOffset receivedAtUtc,
        IEnumerable<EvidenceReferenceInput> evidenceReferences)
    {
        Id = id;
        CaseId = caseId;
        ReporterId = RequireActor(reporterId);
        ResourceKind = resource.Kind;
        ResourceId = resource.ResourceId;
        Reason = reason;
        IdempotencyKeyHash = RequireSha256(idempotencyKeyHash, nameof(idempotencyKeyHash));
        RequestHash = RequireSha256(requestHash, nameof(requestHash));
        ReceivedAtUtc = receivedAtUtc;

        foreach (var evidence in evidenceReferences)
            _evidenceReferences.Add(EvidenceReference.Create(id, evidence));

        if (_evidenceReferences.Count > 16)
            throw new ModerationValidationException("A report may reference at most 16 evidence records.");
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public string ReporterId { get; private set; } = string.Empty;
    public ResourceKind ResourceKind { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public ReportReason Reason { get; private set; }
    public string IdempotencyKeyHash { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public IReadOnlyCollection<EvidenceReference> EvidenceReferences => _evidenceReferences;

    public static ModerationReport Create(
        Guid id,
        Guid caseId,
        string reporterId,
        ResourceReference resource,
        ReportReason reason,
        string idempotencyKeyHash,
        string requestHash,
        DateTimeOffset receivedAtUtc,
        IEnumerable<EvidenceReferenceInput> evidenceReferences)
    {
        if (id == Guid.Empty || caseId == Guid.Empty)
            throw new ModerationValidationException("Stable report and case identifiers are required.");
        if (!Enum.IsDefined(reason))
            throw new ModerationValidationException("The report reason is not allow-listed.");

        return new ModerationReport(
            id,
            caseId,
            reporterId,
            resource,
            reason,
            idempotencyKeyHash,
            requestHash,
            receivedAtUtc,
            evidenceReferences ?? []);
    }

    internal static string RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128)
            throw new ModerationValidationException("A bounded actor identifier is required.");
        return actor.Trim();
    }

    internal static string RequireSha256(string value, string parameter)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ModerationValidationException($"{parameter} must be a SHA-256 hexadecimal digest.");
        return value.ToLowerInvariant();
    }
}
