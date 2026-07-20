using System.Security.Cryptography;
using System.Text;

namespace Moderation.Infra.Providers;

internal static class ModerationMessageIdentity
{
    public static Guid Create(string messageKind, Guid caseId, long version)
    {
        if (string.IsNullOrWhiteSpace(messageKind))
            throw new ArgumentException("A message kind is required.", nameof(messageKind));
        if (caseId == Guid.Empty)
            throw new ArgumentException("A case ID is required.", nameof(caseId));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        var material = Encoding.UTF8.GetBytes(
            $"moderation:{messageKind.Trim().ToLowerInvariant()}:{caseId:D}:{version}");
        var bytes = SHA256.HashData(material)[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
