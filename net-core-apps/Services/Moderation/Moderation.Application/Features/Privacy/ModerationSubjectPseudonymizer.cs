using System.Globalization;
using System.Text;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;
using Moderation.Application.Options;
using Moderation.Domain.AggregatesModel;

namespace Moderation.Application.Features.Privacy;

/// <summary>
/// Produces a non-reversible, service-scoped lookup value that lets an erasure
/// locate a retained case after its directly identifying reporter ID is gone.
/// The key is deliberately separate from authentication and cursor keys.
/// </summary>
public sealed class ModerationSubjectPseudonymizer
{
    public const int EncodedLength = HmacSha256.TagSizeInBytes * 2;
    private static ReadOnlySpan<byte> DomainSeparator =>
        "moderation-erasure-subject:v1:"u8;

    private readonly byte[] _key;

    public ModerationSubjectPseudonymizer(
        IOptions<ModerationPrivacyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Base64KeyMaterial.TryDecode(
                options.Value.SubjectLookupKey,
                out _key,
                rejectSurroundingWhitespace: false))
        {
            throw new InvalidOperationException(
                "Moderation privacy requires a base64 subject lookup key of at least 32 bytes.");
        }
    }

    public string Create(long userId)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        return Create(userId.ToString(CultureInfo.InvariantCulture));
    }

    public string Create(string reporterId)
    {
        var normalized = ModerationSubject.Normalize(reporterId);
        var subject = Encoding.UTF8.GetBytes(normalized);
        var material = new byte[DomainSeparator.Length + subject.Length];
        DomainSeparator.CopyTo(material);
        subject.CopyTo(material.AsSpan(DomainSeparator.Length));
        return Convert.ToHexStringLower(HmacSha256.Protect(_key, material));
    }
}
