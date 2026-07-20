using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Moderation.Domain.Exceptions;
using System.Text.RegularExpressions;

namespace Moderation.Domain.AggregatesModel;

public sealed class ModerationAuditEntry
{
    private static readonly Regex CodePattern = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private ModerationAuditEntry() { }

    private ModerationAuditEntry(
        Guid caseId,
        string actorId,
        string action,
        string reasonCode,
        long caseVersion,
        string previousHash,
        DateTimeOffset occurredAtUtc)
    {
        Id = Guid.NewGuid();
        CaseId = caseId;
        ActorId = ModerationReport.RequireActor(actorId);
        Action = RequireCode(action, nameof(action));
        ReasonCode = RequireCode(reasonCode, nameof(reasonCode));
        CaseVersion = caseVersion;
        PreviousHash = previousHash;
        OccurredAtUtc = occurredAtUtc;
        Hash = ComputeHash(caseId, ActorId, Action, ReasonCode, caseVersion, previousHash, occurredAtUtc);
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public long CaseVersion { get; private set; }
    public string PreviousHash { get; private set; } = string.Empty;
    public string Hash { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    internal static ModerationAuditEntry Create(
        Guid caseId,
        string actorId,
        string action,
        string reasonCode,
        long caseVersion,
        string previousHash,
        DateTimeOffset occurredAtUtc) =>
        new(caseId, actorId, action, reasonCode, caseVersion, previousHash, occurredAtUtc);

    public bool HasValidHash() =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Hash),
            Convert.FromHexString(ComputeHash(
                CaseId,
                ActorId,
                Action,
                ReasonCode,
                CaseVersion,
                PreviousHash,
                OccurredAtUtc)));

    private static string RequireCode(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || !CodePattern.IsMatch(code))
            throw new ModerationValidationException($"{name} must be a bounded machine-readable code.");
        return code;
    }

    private static string ComputeHash(
        Guid caseId,
        string actorId,
        string action,
        string reasonCode,
        long caseVersion,
        string previousHash,
        DateTimeOffset occurredAtUtc)
    {
        var canonical = string.Join(
            '\n',
            caseId.ToString("D"),
            actorId,
            action,
            reasonCode,
            caseVersion.ToString(CultureInfo.InvariantCulture),
            previousHash,
            occurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
