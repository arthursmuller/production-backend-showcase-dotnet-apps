using Feed.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Feed.Application.Features.Feeds
{
    public sealed class FeedCursorOptions
    {
        public const string SectionName = "FeedCursor";

        public string ActiveKeyId { get; set; } = string.Empty;
        public Dictionary<string, string> SigningKeys { get; set; } = new(StringComparer.Ordinal);
        public int LifetimeSeconds { get; set; } = 900;
        public int ClockSkewSeconds { get; set; } = 15;
    }

    public static class FeedCursorOptionsValidation
    {
        public static bool IsValid(FeedCursorOptions options)
        {
            if (options is null
                || !IsValidKeyId(options.ActiveKeyId)
                || options.SigningKeys is null
                || options.SigningKeys.Count is < 1 or > 8
                || !options.SigningKeys.ContainsKey(options.ActiveKeyId)
                || options.LifetimeSeconds is < 60 or > 86_400
                || options.ClockSkewSeconds is < 0 or > 300
                || options.ClockSkewSeconds >= options.LifetimeSeconds)
            {
                return false;
            }

            foreach (var key in options.SigningKeys)
            {
                if (!IsValidKeyId(key.Key)
                    || !TryDecodeSigningKey(key.Value, out _))
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool TryDecodeSigningKey(string encoded, out byte[] key)
        {
            key = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 256)
                return false;
            try
            {
                key = Convert.FromBase64String(encoded);
                return key.Length is >= 32 and <= 128;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool IsValidKeyId(string keyId) =>
            !string.IsNullOrWhiteSpace(keyId)
            && keyId.Length <= 32
            && keyId.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_');
    }

    public static class FeedCursorSurfaces
    {
        public const string Items = "items";
        public const string FixedSort = "rank_desc,published_desc,activity_desc";
    }

    public sealed class FeedCursorRejectedException : FeedException
    {
        public FeedCursorRejectedException()
            : base("The feed cursor is invalid, expired, or does not match this request.", 400)
        {
        }
    }

    /// <summary>
    /// HMAC-SHA256 Feed cursor. The signed scope prevents a continuation from being
    /// replayed by another viewer or with a different revision, snapshot, page size, filter, or
    /// sort. Key IDs permit bounded overlap during rotation; unsigned cursors are rejected.
    /// </summary>
    public static class FeedCursorCodec
    {
        private const int SignatureBytes = 32;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        };

        public static string Encode(
            FeedCursor cursor,
            int viewerUserId,
            long feedRevision,
            int pageSize,
            bool includeSeen,
            string filterSurface,
            FeedCursorOptions options,
            DateTimeOffset issuedAtUtc,
            int maximumLength)
        {
            ValidateConfiguration(options, maximumLength);
            ValidateCursor(cursor);
            ValidateScopeInput(viewerUserId, feedRevision, pageSize, filterSurface);
            var issuedAt = issuedAtUtc.ToUnixTimeSeconds();
            var expiresAt = checked(issuedAt + options.LifetimeSeconds);
            var fingerprint = ScopeFingerprint(
                viewerUserId,
                feedRevision,
                cursor.SnapshotUtcTicks,
                pageSize,
                includeSeen,
                filterSurface);
            var payload = new CursorPayload(
                issuedAt,
                expiresAt,
                cursor.SnapshotUtcTicks,
                cursor.HighWatermark.RankKey,
                cursor.HighWatermark.PublishedUtcTicks,
                cursor.HighWatermark.ActivityId,
                cursor.Last.RankKey,
                cursor.Last.PublishedUtcTicks,
                cursor.Last.ActivityId,
                fingerprint);
            var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
            var signed = $"{options.ActiveKeyId}.{payloadSegment}";
            var signature = ComputeSignature(options, options.ActiveKeyId, signed);
            var encoded = $"{signed}.{Base64UrlEncode(signature)}";
            if (encoded.Length > maximumLength)
                throw new FeedCursorRejectedException();
            return encoded;
        }

        public static FeedCursor Decode(
            string encoded,
            int viewerUserId,
            long feedRevision,
            int pageSize,
            bool includeSeen,
            string filterSurface,
            FeedCursorOptions options,
            DateTimeOffset nowUtc,
            int maximumLength)
        {
            ValidateConfiguration(options, maximumLength);
            ValidateScopeInput(viewerUserId, feedRevision, pageSize, filterSurface);
            if (string.IsNullOrWhiteSpace(encoded)
                || encoded.Length > maximumLength
                || encoded.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.'))
            {
                throw new FeedCursorRejectedException();
            }

            try
            {
                var segments = encoded.Split('.');
                if (segments.Length != 3
                    || !options.SigningKeys.ContainsKey(segments[0]))
                {
                    throw new FeedCursorRejectedException();
                }

                var suppliedSignature = Base64UrlDecode(segments[2]);
                if (suppliedSignature.Length != SignatureBytes)
                    throw new FeedCursorRejectedException();
                var signed = $"{segments[0]}.{segments[1]}";
                var expectedSignature = ComputeSignature(options, segments[0], signed);
                if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
                    throw new FeedCursorRejectedException();

                var payloadBytes = Base64UrlDecode(segments[1]);
                if (payloadBytes.Length is < 32 or > 1_024)
                    throw new FeedCursorRejectedException();
                var payload = JsonSerializer.Deserialize<CursorPayload>(payloadBytes, JsonOptions)
                    ?? throw new FeedCursorRejectedException();
                var cursor = new FeedCursor(
                    payload.SnapshotUtcTicks,
                    new FeedSortTuple(
                        payload.HighRankKey,
                        payload.HighPublishedUtcTicks,
                        payload.HighActivityId),
                    new FeedSortTuple(
                        payload.LastRankKey,
                        payload.LastPublishedUtcTicks,
                        payload.LastActivityId));
                ValidateCursor(cursor);

                var now = nowUtc.ToUnixTimeSeconds();
                var nowTicks = nowUtc.UtcDateTime.Ticks;
                var skewTicks = TimeSpan.FromSeconds(options.ClockSkewSeconds).Ticks;
                var latestSnapshotTicks = nowTicks > DateTime.MaxValue.Ticks - skewTicks
                    ? DateTime.MaxValue.Ticks
                    : checked(nowTicks + skewTicks);
                if (payload.IssuedAtUnixSeconds > checked(now + options.ClockSkewSeconds)
                    || payload.ExpiresAtUnixSeconds < checked(now - options.ClockSkewSeconds)
                    || payload.ExpiresAtUnixSeconds <= payload.IssuedAtUnixSeconds
                    || payload.ExpiresAtUnixSeconds - payload.IssuedAtUnixSeconds > options.LifetimeSeconds
                    || payload.SnapshotUtcTicks > latestSnapshotTicks)
                {
                    throw new FeedCursorRejectedException();
                }

                var expectedFingerprint = ScopeFingerprint(
                    viewerUserId,
                    feedRevision,
                    payload.SnapshotUtcTicks,
                    pageSize,
                    includeSeen,
                    filterSurface);
                var suppliedFingerprint = Base64UrlDecode(payload.ScopeFingerprint);
                var expectedFingerprintBytes = Base64UrlDecode(expectedFingerprint);
                if (suppliedFingerprint.Length != expectedFingerprintBytes.Length
                    || !CryptographicOperations.FixedTimeEquals(
                        expectedFingerprintBytes,
                        suppliedFingerprint))
                {
                    throw new FeedCursorRejectedException();
                }
                return cursor;
            }
            catch (FeedCursorRejectedException)
            {
                throw;
            }
            catch (Exception exception) when (exception is FormatException
                or JsonException
                or OverflowException
                or ArgumentException)
            {
                throw new FeedCursorRejectedException();
            }
        }

        private static string ScopeFingerprint(
            int viewerUserId,
            long feedRevision,
            long snapshotUtcTicks,
            int pageSize,
            bool includeSeen,
            string filterSurface)
        {
            var canonical = string.Join(
                '\n',
                "scope",
                viewerUserId.ToString(CultureInfo.InvariantCulture),
                feedRevision.ToString(CultureInfo.InvariantCulture),
                snapshotUtcTicks.ToString(CultureInfo.InvariantCulture),
                pageSize.ToString(CultureInfo.InvariantCulture),
                includeSeen ? "1" : "0",
                filterSurface,
                FeedCursorSurfaces.FixedSort);
            return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        private static void ValidateConfiguration(FeedCursorOptions options, int maximumLength)
        {
            if (!FeedCursorOptionsValidation.IsValid(options)
                || maximumLength is < 128 or > 2_048)
            {
                throw new InvalidOperationException("Feed cursor security is not configured safely.");
            }
        }

        private static void ValidateScopeInput(
            int viewerUserId,
            long feedRevision,
            int pageSize,
            string filterSurface)
        {
            if (viewerUserId <= 0
                || feedRevision < 0
                || pageSize is < 1 or > 50
                || string.IsNullOrWhiteSpace(filterSurface)
                || filterSurface.Length > 32
                || filterSurface.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_'))
            {
                throw new FeedCursorRejectedException();
            }
        }

        private static void ValidateCursor(FeedCursor cursor)
        {
            if (cursor is null
                || !IsValidTicks(cursor.SnapshotUtcTicks)
                || !IsValidTuple(cursor.HighWatermark)
                || !IsValidTuple(cursor.Last)
                || cursor.Last.CompareTo(cursor.HighWatermark) > 0)
            {
                throw new FeedCursorRejectedException();
            }
        }

        private static bool IsValidTuple(FeedSortTuple tuple) =>
            tuple.ActivityId > 0 && IsValidTicks(tuple.PublishedUtcTicks);

        private static bool IsValidTicks(long ticks) =>
            ticks >= DateTime.UnixEpoch.Ticks && ticks <= DateTime.MaxValue.Ticks;

        private static byte[] DecodeConfiguredKey(FeedCursorOptions options, string keyId)
        {
            if (!options.SigningKeys.TryGetValue(keyId, out var encoded)
                || !FeedCursorOptionsValidation.TryDecodeSigningKey(encoded, out var key))
            {
                throw new InvalidOperationException("Feed cursor security is not configured safely.");
            }
            return key;
        }

        private static byte[] ComputeSignature(
            FeedCursorOptions options,
            string keyId,
            string signedValue)
        {
            var key = DecodeConfiguredKey(options, keyId);
            try
            {
                return HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(signedValue));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static string Base64UrlEncode(byte[] value) =>
            Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_'))
            {
                throw new FormatException("Invalid base64url value.");
            }
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return Convert.FromBase64String(base64);
        }

        private sealed record CursorPayload(
            [property: JsonPropertyName("iat")] long IssuedAtUnixSeconds,
            [property: JsonPropertyName("exp")] long ExpiresAtUnixSeconds,
            [property: JsonPropertyName("s")] long SnapshotUtcTicks,
            [property: JsonPropertyName("hr")] long HighRankKey,
            [property: JsonPropertyName("hp")] long HighPublishedUtcTicks,
            [property: JsonPropertyName("hi")] long HighActivityId,
            [property: JsonPropertyName("lr")] long LastRankKey,
            [property: JsonPropertyName("lp")] long LastPublishedUtcTicks,
            [property: JsonPropertyName("li")] long LastActivityId,
            [property: JsonPropertyName("f")] string ScopeFingerprint);
    }
}
