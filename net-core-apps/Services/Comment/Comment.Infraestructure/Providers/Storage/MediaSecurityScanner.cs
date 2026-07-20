using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Exceptions;
using Inspacy.Observability;
using Microsoft.Extensions.Logging;
using StarterEntities.Storage;
using System.Buffers;
using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Storage
{
    public sealed class MediaSecurityScanner(
        MediaSecurityOptions options,
        ClamAvResiliencePolicy clamAv,
        ILogger<MediaSecurityScanner> logger,
        TimeProvider? timeProvider = null) : IMediaSecurityScanner
    {
        private const int HeaderBytes = 32;
        private const int ReadBufferBytes = 64 * 1024;
        private const int MaximumImageDimension = 8_192;
        private const long MaximumDecodedImagePixels = 40_000_000;
        private const long MaximumJpegHeaderBytes = 1024 * 1024;
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly Meter Meter = PlatformInstrumentation.Meter;
        private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
            "inspacy.media.scan.outcomes",
            unit: "{file}");
        private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
            "inspacy.media.scan.duration",
            unit: "ms");
        private static readonly Histogram<long> Sizes = Meter.CreateHistogram<long>(
            "inspacy.media.scan.size",
            unit: "By");

        public async Task<VerifiedMediaContent> VerifyAsync(
            IncomingMediaFile file,
            Stream content,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(content);
            cancellationToken.ThrowIfCancellationRequested();
            CommentMediaRules.EnsureValid(
                file.FileName,
                file.MimeType,
                file.SizeBytes,
                file.MediaType);
            if (!content.CanRead || !content.CanSeek)
                throw new CommentException("Media content must be a readable, seekable stream.");

            var started = Stopwatch.GetTimestamp();
            try
            {
                var verified = await InspectContentAsync(file, content, cancellationToken);
                if (options.RequireMalwareScan)
                {
                    MalwareScanResult scan;
                    try
                    {
                        scan = await clamAv.ScanAsync(content, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (MediaScanDependencyUnavailableException error)
                    {
                        logger.LogWarning(
                            "Media malware scanning dependency was unavailable ({FailureReason}).",
                            error.Reason);
                        Outcomes.Add(1, new KeyValuePair<string, object?>(
                            "outcome",
                            DependencyOutcome(error.Reason)));
                        throw new CommentException(
                            "Media scanning is temporarily unavailable.",
                            503);
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(
                            error,
                            "Media malware scanning failed unexpectedly.");
                        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", "unavailable"));
                        throw new CommentException(
                            "Media scanning is temporarily unavailable.",
                            503);
                    }

                    if (scan.Verdict != MalwareScanVerdict.Clean)
                    {
                        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", "rejected"));
                        throw new CommentException(
                            "Media content was rejected by security scanning.",
                            422);
                    }

                    verified = verified with
                    {
                        Scanner = scan.Scanner,
                        ScannerVersion = scan.EngineVersion,
                        SignatureVersion = scan.SignatureVersion,
                        ScannedAtUtc = (timeProvider ?? TimeProvider.System)
                            .GetUtcNow().UtcDateTime,
                    };
                }

                Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", "clean"));
                Sizes.Record(verified.SizeBytes);
                return verified;
            }
            catch (CommentException)
            {
                throw;
            }
            finally
            {
                content.Position = 0;
                Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        private static string DependencyOutcome(MediaScanDependencyFailure reason) => reason switch
        {
            MediaScanDependencyFailure.Saturated => "saturated",
            MediaScanDependencyFailure.CircuitOpen => "circuit_open",
            MediaScanDependencyFailure.Timeout => "timeout",
            _ => "unavailable",
        };

        private static async Task<VerifiedMediaContent> InspectContentAsync(
            IncomingMediaFile file,
            Stream content,
            CancellationToken cancellationToken)
        {
            var safeName = ValidateFileName(file.FileName);
            var header = new byte[HeaderBytes];
            var headerLength = 0;
            var total = 0L;
            var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                content.Position = 0;
                while (true)
                {
                    var read = await content.ReadAsync(
                        buffer.AsMemory(0, ReadBufferBytes),
                        cancellationToken);
                    if (read == 0)
                        break;
                    total = checked(total + read);
                    if (total > file.SizeBytes)
                        throw new CommentException("Media length does not match the multipart metadata.");
                    if (headerLength < header.Length)
                    {
                        var copy = Math.Min(read, header.Length - headerLength);
                        buffer.AsSpan(0, copy).CopyTo(header.AsSpan(headerLength));
                        headerLength += copy;
                    }
                    hash.AppendData(buffer, 0, read);
                }

                if (total != file.SizeBytes)
                    throw new CommentException("Media length does not match the multipart metadata.");
                var detected = Detect(header.AsSpan(0, headerLength));
                if (detected is null
                    || !detected.MimeType.Equals(file.MimeType, StringComparison.OrdinalIgnoreCase)
                    || detected.MediaType != file.MediaType)
                {
                    throw new CommentException(
                        "Media content does not match its declared MIME and media type.");
                }
                if (detected.MediaType == MediaType.Image)
                    ValidateImageExpansion(detected, content, cancellationToken);

                var suppliedExtension = Path.GetExtension(safeName).ToLowerInvariant();
                if (!detected.AllowedExtensions.Contains(suppliedExtension, StringComparer.Ordinal))
                    throw new CommentException("Media filename extension does not match its content.");

                return new VerifiedMediaContent(
                    safeName,
                    detected.MimeType,
                    detected.CanonicalExtension,
                    total,
                    Convert.ToHexStringLower(hash.GetHashAndReset()),
                    "content-signature-gate",
                    typeof(MediaSecurityScanner).Assembly.GetName().Version?.ToString()
                        ?? "not-reported",
                    "development-scan-disabled",
                    DateTime.UtcNow);
            }
            finally
            {
                content.Position = 0;
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        private static string ValidateFileName(string supplied)
        {
            var normalized = supplied.Trim().Normalize(NormalizationForm.FormC);
            var safeName = Path.GetFileName(normalized);
            if (!string.Equals(normalized, safeName, StringComparison.Ordinal)
                || safeName.Length is < 1 or > 255
                || safeName.Any(character => char.IsControl(character) || character is '/' or '\\'))
            {
                throw new CommentException("Media filename is invalid.");
            }
            return safeName;
        }

        private static DetectedMedia? Detect(ReadOnlySpan<byte> header)
        {
            if (header.StartsWith(PngSignature))
            {
                if (header.Length < 24 || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
                    return null;
                var width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
                return new("image/png", ".png", [".png"], MediaType.Image, width, height);
            }
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return new("image/jpeg", ".jpg", [".jpg", ".jpeg"], MediaType.Image);
            if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
            {
                if (header.Length < 10)
                    return null;
                var width = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
                var height = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
                return new("image/gif", ".gif", [".gif"], MediaType.Image, width, height);
            }
            if (header.Length >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header.Slice(8, 4).SequenceEqual("WEBP"u8))
                return DetectWebP(header);
            if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
                return new("video/mp4", ".mp4", [".mp4"], MediaType.Video);
            if (header.StartsWith("ID3"u8)
                || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0))
                return new("audio/mpeg", ".mp3", [".mp3"], MediaType.Audio);
            if (header.Length >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header.Slice(8, 4).SequenceEqual("WAVE"u8))
                return new("audio/wav", ".wav", [".wav"], MediaType.Audio);
            if (header.StartsWith("OggS"u8))
                return new("audio/ogg", ".ogg", [".ogg", ".oga"], MediaType.Audio);
            return null;
        }

        private static DetectedMedia? DetectWebP(ReadOnlySpan<byte> header)
        {
            if (header.Length >= 30 && header.Slice(12, 4).SequenceEqual("VP8X"u8))
            {
                // Animated WebP requires a dedicated sandboxed decoder/transcoder and is
                // rejected at this inline gate to keep expansion work bounded.
                if ((header[20] & 0x02) != 0)
                    return null;
                var width = 1U + ReadUInt24LittleEndian(header.Slice(24, 3));
                var height = 1U + ReadUInt24LittleEndian(header.Slice(27, 3));
                return new("image/webp", ".webp", [".webp"], MediaType.Image, width, height);
            }
            if (header.Length >= 30
                && header.Slice(12, 4).SequenceEqual("VP8 "u8)
                && header.Slice(23, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
            {
                var width = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(26, 2)) & 0x3FFF);
                var height = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(28, 2)) & 0x3FFF);
                return new("image/webp", ".webp", [".webp"], MediaType.Image, width, height);
            }
            if (header.Length >= 25
                && header.Slice(12, 4).SequenceEqual("VP8L"u8)
                && header[20] == 0x2F)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(21, 4));
                var width = 1U + (bits & 0x3FFF);
                var height = 1U + ((bits >> 14) & 0x3FFF);
                return new("image/webp", ".webp", [".webp"], MediaType.Image, width, height);
            }
            return null;
        }

        private static uint ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
            (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16);

        private static void ValidateImageExpansion(
            DetectedMedia detected,
            Stream content,
            CancellationToken cancellationToken)
        {
            var width = detected.Width;
            var height = detected.Height;
            if (!width.HasValue || !height.HasValue)
                (width, height) = ReadJpegDimensions(content, cancellationToken);

            if (width is null or 0 || height is null or 0
                || width > MaximumImageDimension
                || height > MaximumImageDimension
                || checked((long)width.Value * height.Value) > MaximumDecodedImagePixels)
            {
                throw new CommentException(
                    "Media image dimensions exceed the safe decoding envelope.");
            }
        }

        private static (uint? Width, uint? Height) ReadJpegDimensions(
            Stream content,
            CancellationToken cancellationToken)
        {
            content.Position = 2;
            var inspected = 2L;
            var inspectionLimit = Math.Min(content.Length, MaximumJpegHeaderBytes);
            while (inspected < inspectionLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = content.ReadByte();
                inspected++;
                if (prefix != 0xFF)
                    continue;

                int marker;
                do
                {
                    marker = content.ReadByte();
                    inspected++;
                } while (marker == 0xFF && inspected < inspectionLimit);
                if (marker < 0)
                    break;
                if (marker is 0x01 or >= 0xD0 and <= 0xD9)
                    continue;

                var high = content.ReadByte();
                var low = content.ReadByte();
                inspected += 2;
                if (high < 0 || low < 0)
                    break;
                var segmentLength = (high << 8) | low;
                if (segmentLength < 2 || inspected + segmentLength - 2 > inspectionLimit)
                    break;

                if (IsJpegStartOfFrame(marker))
                {
                    if (segmentLength < 7)
                        break;
                    _ = content.ReadByte();
                    var heightHigh = content.ReadByte();
                    var heightLow = content.ReadByte();
                    var widthHigh = content.ReadByte();
                    var widthLow = content.ReadByte();
                    if (heightHigh < 0 || heightLow < 0 || widthHigh < 0 || widthLow < 0)
                        break;
                    return (
                        checked((uint)((widthHigh << 8) | widthLow)),
                        checked((uint)((heightHigh << 8) | heightLow)));
                }

                content.Position += segmentLength - 2;
                inspected += segmentLength - 2;
            }

            return (null, null);
        }

        private static bool IsJpegStartOfFrame(int marker) => marker is
            0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or
            0xCD or 0xCE or 0xCF;

        private sealed record DetectedMedia(
            string MimeType,
            string CanonicalExtension,
            string[] AllowedExtensions,
            MediaType MediaType,
            uint? Width = null,
            uint? Height = null);
    }
}
