using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public sealed record EvidenceReferenceInput(EvidenceKind Kind, string Reference, string Sha256);

/// <summary>
/// Immutable evidence metadata owned by a moderation report. Evidence payloads remain in
/// separately authorized storage; the domain keeps only a bounded opaque reference and digest.
/// </summary>
public sealed class EvidenceReference
{
    private EvidenceReference() { }

    private EvidenceReference(Guid reportId, EvidenceReferenceInput input)
    {
        if (!Enum.IsDefined(input.Kind))
            throw new ModerationValidationException("The evidence reference kind is not allow-listed.");
        if (string.IsNullOrWhiteSpace(input.Reference) || input.Reference.Length > 256)
            throw new ModerationValidationException("Evidence references must be opaque and at most 256 characters.");
        if (input.Reference.Contains('\n') || input.Reference.Contains('\r'))
            throw new ModerationValidationException("Evidence references cannot contain control lines.");

        Id = Guid.NewGuid();
        ReportId = reportId;
        Kind = input.Kind;
        Reference = input.Reference.Trim();
        Sha256 = ModerationReport.RequireSha256(input.Sha256, nameof(input.Sha256));
    }

    public Guid Id { get; private set; }
    public Guid ReportId { get; private set; }
    public EvidenceKind Kind { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;

    internal static EvidenceReference Create(Guid reportId, EvidenceReferenceInput input) =>
        new(reportId, input);
}
