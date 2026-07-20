using BuildingBlocks.Security;
using Comment.Application.Dtos;
using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using Microsoft.Extensions.Options;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Comment.Application.Features.Comments.Queries
{
    public sealed class CommentCursorOptions
    {
        public const string SectionName = "CommentCursor";
        public string? SigningKey { get; set; }
        public int LifetimeSeconds { get; set; } = 86_400;

        public static bool IsValid(CommentCursorOptions options) =>
            options is not null &&
            options.LifetimeSeconds is >= 300 and <= 604_800 &&
            TryDecodeKey(options.SigningKey, out _);

        internal static bool TryDecodeKey(string? encoded, out byte[] key)
            => Base64KeyMaterial.TryDecode(encoded, out key);
    }

    public sealed record CommentCursorPage<T>(
        IReadOnlyList<T> Items,
        string? NextCursor,
        bool HasMore);

    /// <summary>
    /// HMAC-protected cursor. A cursor is a traversal position rather than
    /// an authorization grant, but signing prevents attacker-selected arbitrary payloads
    /// and binds the position to the original filter/scope fingerprint.
    /// </summary>
    public sealed class CommentCursorCodec
    {
        public const int FingerprintLength = 16;
        public const int MaximumEncodedLength = 128;
        private const int PayloadLength = sizeof(long) + sizeof(long) + sizeof(int) + FingerprintLength;
        private const int SignatureLength = 32;
        private const int CursorLength = PayloadLength + SignatureLength;

        private readonly byte[] _key;
        private readonly int _lifetimeSeconds;
        private readonly TimeProvider _timeProvider;

        public CommentCursorCodec(
            IOptions<CommentCursorOptions> options,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            var value = options.Value;
            if (!CommentCursorOptions.IsValid(value) ||
                !CommentCursorOptions.TryDecodeKey(value.SigningKey, out _key))
            {
                throw new InvalidOperationException(
                    "CommentCursor configuration requires a valid 256-bit-or-stronger signing key and bounded lifetime.");
            }

            _lifetimeSeconds = value.LifetimeSeconds;
        }

        public string Encode(
            CommentCursorPosition position,
            byte[] filterFingerprint)
        {
            ArgumentNullException.ThrowIfNull(position);
            ValidateFingerprint(filterFingerprint);
            if (position.Id <= 0)
                throw new ArgumentOutOfRangeException(nameof(position));

            var createdDate = NormalizeUtc(position.CreatedDateUtc);
            var payload = new byte[PayloadLength];
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(0, sizeof(long)),
                _timeProvider.GetUtcNow().ToUnixTimeSeconds());
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(sizeof(long), sizeof(long)),
                createdDate.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(2 * sizeof(long), sizeof(int)),
                position.Id);
            filterFingerprint.CopyTo(
                payload,
                (2 * sizeof(long)) + sizeof(int));

            var signature = HmacSha256.Protect(_key, payload);
            var cursor = new byte[CursorLength];
            payload.CopyTo(cursor, 0);
            signature.CopyTo(cursor, PayloadLength);
            return Base64Url.Encode(cursor);
        }

        public bool TryDecode(
            string? encoded,
            byte[] expectedFilterFingerprint,
            out CommentCursorPosition? position)
        {
            position = null;
            if (!IsFingerprint(expectedFilterFingerprint) ||
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
                expectedFilterFingerprint))
                return false;

            var issuedAt = BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(0, sizeof(long)));
            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (issuedAt > now + 300 || now - issuedAt > _lifetimeSeconds)
                return false;

            var createdTicks = BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(sizeof(long), sizeof(long)));
            var id = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(2 * sizeof(long), sizeof(int)));
            if (id <= 0 || createdTicks < DateTime.MinValue.Ticks || createdTicks > DateTime.MaxValue.Ticks)
                return false;

            position = new CommentCursorPosition(
                new DateTime(createdTicks, DateTimeKind.Utc),
                id);
            return true;
        }

        private static void ValidateFingerprint(byte[] fingerprint)
        {
            if (!IsFingerprint(fingerprint))
                throw new ArgumentException(
                    $"A {FingerprintLength}-byte filter fingerprint is required.",
                    nameof(fingerprint));
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

    public static class CommentCursorFingerprint
    {
        public static byte[] Create(CommentsCriteria criteria, string scope)
        {
            criteria ??= new CommentsCriteria();
            if (string.IsNullOrWhiteSpace(scope) || scope.Length > 64 ||
                criteria.Ids is { Length: > 100 } ||
                criteria.Targets is { Length: > 100 })
                throw new CommentException("The comment cursor filters are invalid.");

            using var stream = new MemoryStream(capacity: 512);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(scope);
                writer.Write(criteria.UserId ?? 0);
                WriteDate(writer, criteria.DateRange, 0);
                WriteDate(writer, criteria.DateRange, 1);

                var ids = criteria.Ids?
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray() ?? Array.Empty<int>();
                writer.Write(ids.Length);
                foreach (var id in ids)
                    writer.Write(id);

                var targets = criteria.Targets?
                    .Where(target => target is not null)
                    .Select(target => new
                    {
                        TargetId = target.TargetId?.Trim() ?? string.Empty,
                        TargetType = target.TargetType?.Trim().ToLowerInvariant() ?? string.Empty,
                    })
                    .Distinct()
                    .OrderBy(target => target.TargetType, StringComparer.Ordinal)
                    .ThenBy(target => target.TargetId, StringComparer.Ordinal)
                    .ToArray() ?? [];
                writer.Write(targets.Length);
                foreach (var target in targets)
                {
                    writer.Write(target.TargetType);
                    writer.Write(target.TargetId);
                }
            }

            return SHA256.HashData(stream.ToArray())
                .AsSpan(0, CommentCursorCodec.FingerprintLength)
                .ToArray();
        }

        private static void WriteDate(
            BinaryWriter writer,
            DateTime?[]? range,
            int index)
        {
            if (range is null || range.Length <= index || range[index] is not { } value)
            {
                writer.Write(long.MinValue);
                return;
            }

            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
            writer.Write(utc.Ticks);
        }
    }
}
