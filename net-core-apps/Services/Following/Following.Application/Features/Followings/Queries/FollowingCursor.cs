using BuildingBlocks.Security;
using Following.Domain.Repositories;
using Microsoft.Extensions.Options;
using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Following.Application.Features.Followings
{
    public sealed class FollowingCursorOptions
    {
        public const string SectionName = "FollowingCursor";
        public string SigningKey { get; set; } = string.Empty;
        public int LifetimeSeconds { get; set; } = 86_400;

        public static bool IsValid(FollowingCursorOptions options) =>
            options is not null &&
            options.LifetimeSeconds is >= 300 and <= 604_800 &&
            TryDecodeKey(options.SigningKey, out _);

        internal static bool TryDecodeKey(string encoded, out byte[] key)
            => Base64KeyMaterial.TryDecode(encoded, out key);
    }

    /// <summary>
    /// Signed and expiring HMAC cursor. It is a traversal position, not an
    /// authorization grant; every request still executes its normal policy and
    /// the signature binds the position to actor, surface, filter, and fixed sort.
    /// </summary>
    public sealed class FollowingCursorCodec
    {
        public const int FingerprintLength = 16;
        public const int MaximumEncodedLength = 128;
        private const int PayloadLength =
            sizeof(long) + sizeof(long) + sizeof(int) + FingerprintLength;
        private const int SignatureLength = 32;
        private const int CursorLength = PayloadLength + SignatureLength;

        private readonly byte[] _key;
        private readonly int _lifetimeSeconds;
        private readonly TimeProvider _timeProvider;

        public FollowingCursorCodec(
            IOptions<FollowingCursorOptions> options,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            var value = options.Value;
            if (!FollowingCursorOptions.IsValid(value) ||
                !FollowingCursorOptions.TryDecodeKey(value.SigningKey, out _key))
            {
                throw new InvalidOperationException(
                    "FollowingCursor configuration requires a base64-encoded " +
                    "256-bit-or-stronger signing key and a lifetime from 300 to 604800 seconds.");
            }

            _lifetimeSeconds = value.LifetimeSeconds;
        }

        public string Encode(
            FollowingCursorPosition position,
            byte[] scopeFingerprint)
        {
            ArgumentNullException.ThrowIfNull(position);
            ValidateFingerprint(scopeFingerprint);
            if (position.Id <= 0)
                throw new ArgumentOutOfRangeException(nameof(position));

            var payload = new byte[PayloadLength];
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(0, sizeof(long)),
                _timeProvider.GetUtcNow().ToUnixTimeSeconds());
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(sizeof(long), sizeof(long)),
                NormalizeUtc(position.CreatedDateUtc).Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(2 * sizeof(long), sizeof(int)),
                position.Id);
            scopeFingerprint.CopyTo(
                payload,
                (2 * sizeof(long)) + sizeof(int));

            var signature = HmacSha256.Protect(_key, payload);
            var cursor = new byte[CursorLength];
            payload.CopyTo(cursor, 0);
            signature.CopyTo(cursor, PayloadLength);
            return Base64Url.Encode(cursor);
        }

        public bool TryDecode(
            string encoded,
            byte[] expectedScopeFingerprint,
            [NotNullWhen(true)] out FollowingCursorPosition? position)
        {
            position = null;
            if (!IsFingerprint(expectedScopeFingerprint) ||
                string.IsNullOrWhiteSpace(encoded) ||
                encoded.Length > MaximumEncodedLength ||
                !Base64Url.TryDecode(encoded, out var cursor, MaximumEncodedLength) ||
                cursor.Length != CursorLength)
                return false;

            var payload = cursor.AsSpan(0, PayloadLength);
            var suppliedSignature = cursor.AsSpan(PayloadLength, SignatureLength);
            if (!HmacSha256.Verify(_key, payload, suppliedSignature))
                return false;

            var fingerprintOffset = (2 * sizeof(long)) + sizeof(int);
            if (!CryptographicOperations.FixedTimeEquals(
                payload.Slice(fingerprintOffset, FingerprintLength),
                expectedScopeFingerprint))
                return false;

            var issuedAt = BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(0, sizeof(long)));
            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (issuedAt > now + 300 || now - issuedAt > _lifetimeSeconds)
                return false;

            var ticks = BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(sizeof(long), sizeof(long)));
            var id = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(2 * sizeof(long), sizeof(int)));
            if (id <= 0 || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;

            position = new FollowingCursorPosition(
                new DateTime(ticks, DateTimeKind.Utc),
                id);
            return true;
        }

        private static void ValidateFingerprint(byte[] fingerprint)
        {
            if (!IsFingerprint(fingerprint))
            {
                throw new ArgumentException(
                    $"A {FingerprintLength}-byte cursor scope fingerprint is required.",
                    nameof(fingerprint));
            }
        }

        private static bool IsFingerprint(byte[] fingerprint) =>
            fingerprint is { Length: FingerprintLength };

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    }

    public static class FollowingCursorFingerprint
    {
        public static byte[] Create(
            string surface,
            string actorScope,
            params string[] filterParts)
        {
            if (string.IsNullOrWhiteSpace(surface) || surface.Length > 64)
                throw new ArgumentException("A bounded cursor surface is required.", nameof(surface));
            if (string.IsNullOrWhiteSpace(actorScope) || actorScope.Length > 512)
                throw new ArgumentException("A bounded cursor actor scope is required.", nameof(actorScope));
            if (filterParts is null || filterParts.Length > 16 ||
                filterParts.Any(part => part is null || part.Length > 256))
                throw new ArgumentException("Cursor filters are invalid.", nameof(filterParts));

            using var stream = new MemoryStream(capacity: 512);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(surface);
                writer.Write(actorScope);
                writer.Write("date_desc");
                writer.Write(filterParts.Length);
                foreach (var part in filterParts)
                    writer.Write(part);
            }

            return SHA256.HashData(stream.ToArray())
                .AsSpan(0, FollowingCursorCodec.FingerprintLength)
                .ToArray();
        }
    }
}
