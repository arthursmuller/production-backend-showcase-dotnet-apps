using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

/// <summary>
/// Restricted discussion metadata. The protected message body remains in separately authorized
/// object storage; this row contains only its opaque reference and integrity digest.
/// </summary>
public sealed class ModerationCaseMessage
{
    private ModerationCaseMessage() { }

    private ModerationCaseMessage(
        Guid id,
        Guid caseId,
        string actorId,
        string reference,
        string sha256,
        string contentType,
        long caseVersion,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || caseId == Guid.Empty)
            throw new ModerationValidationException("Stable message and case identifiers are required.");
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 256
            || reference.Contains('\n') || reference.Contains('\r'))
            throw new ModerationValidationException("A bounded opaque message reference is required.");
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 100
            || contentType.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '/' and not '-' and not '+' and not '.'))
            throw new ModerationValidationException("A bounded media content type is required.");

        Id = id;
        CaseId = caseId;
        ActorId = ModerationReport.RequireActor(actorId);
        Reference = reference.Trim();
        Sha256 = ModerationReport.RequireSha256(sha256, nameof(sha256));
        ContentType = contentType;
        CaseVersion = caseVersion;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long CaseVersion { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static ModerationCaseMessage Create(
        Guid id,
        Guid caseId,
        string actorId,
        string reference,
        string sha256,
        string contentType,
        long caseVersion,
        DateTimeOffset createdAtUtc) =>
        new(id, caseId, actorId, reference, sha256, contentType, caseVersion, createdAtUtc);
}
