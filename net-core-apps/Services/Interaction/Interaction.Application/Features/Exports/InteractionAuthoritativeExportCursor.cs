using BuildingBlocks.Security;
using Interaction.Application.Options;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace Interaction.Application.Features.Exports;

public sealed record InteractionAuthoritativeExportCursorState(
    long AfterCursor,
    int PageSize,
    InteractionAuthoritativeExportSnapshot Snapshot,
    DateTimeOffset IssuedAtUtc);

public sealed class InteractionAuthoritativeExportCursor
{
    public const int MaximumEncodedLength = 512;
    private const int FormatVersion = 1;
    private const int PayloadLength = 115;
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public InteractionAuthoritativeExportCursor(
        IOptions<AuthoritativeExportOptions> options,
        TimeProvider timeProvider)
    {
        var value = options.Value;
        if (!AuthoritativeExportOptions.IsValid(value)
            || !Base64KeyMaterial.TryDecode(
                value.CursorSigningKey,
                out _key,
                rejectSurroundingWhitespace: false))
        {
            throw new InvalidOperationException("Authoritative export cursor configuration is invalid.");
        }
        _lifetime = TimeSpan.FromMinutes(value.CursorLifetimeMinutes);
        _timeProvider = timeProvider;
    }

    public string Encode(InteractionAuthoritativeExportCursorState state)
    {
        using var stream = new MemoryStream(PayloadLength);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)FormatVersion);
            writer.Write(state.AfterCursor);
            writer.Write((ushort)state.PageSize);
            writer.Write(state.Snapshot.HighWatermark);
            writer.Write(state.Snapshot.RevisionHighWatermark);
            writer.Write(state.Snapshot.CapturedAtUtc.ToUniversalTime().Ticks);
            writer.Write(state.Snapshot.ExpectedItemCount);
            writer.Write(state.Snapshot.MinimumResourceVersion ?? -1);
            writer.Write(state.Snapshot.MaximumResourceVersion ?? -1);
            writer.Write(state.Snapshot.MinimumSummaryVersion ?? -1);
            writer.Write(state.Snapshot.MaximumSummaryVersion ?? -1);
            writer.Write(Convert.FromHexString(state.Snapshot.GlobalXorSha256));
            writer.Write(state.IssuedAtUtc.ToUniversalTime().Ticks);
        }
        var payload = stream.ToArray();
        var signature = HmacSha256.Protect(_key, payload);
        var token = $"{Base64Url.Encode(payload)}.{Base64Url.Encode(signature)}";
        if (token.Length > MaximumEncodedLength)
            throw new InvalidOperationException("The authoritative export cursor exceeded its bound.");
        return token;
    }

    public InteractionAuthoritativeExportCursorState Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaximumEncodedLength)
            throw InvalidCursor();
        var segments = token.Split('.');
        if (segments.Length != 2)
            throw InvalidCursor();

        try
        {
            var payloadBytes = Base64Url.Decode(segments[0]);
            var signature = Base64Url.Decode(segments[1]);
            if (!HmacSha256.Verify(_key, payloadBytes, signature))
                throw InvalidCursor();
            if (payloadBytes.Length != PayloadLength)
                throw InvalidCursor();
            using var stream = new MemoryStream(payloadBytes, writable: false);
            using var reader = new BinaryReader(stream);
            var version = reader.ReadByte();
            var afterCursor = reader.ReadInt64();
            var pageSize = reader.ReadUInt16();
            var highWatermark = reader.ReadInt64();
            var revisionHighWatermark = reader.ReadInt64();
            var capturedAtUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var expectedItemCount = reader.ReadInt64();
            var minimumResourceVersion = NullableVersion(reader.ReadInt64());
            var maximumResourceVersion = NullableVersion(reader.ReadInt64());
            var minimumSummaryVersion = NullableVersion(reader.ReadInt64());
            var maximumSummaryVersion = NullableVersion(reader.ReadInt64());
            var globalXorSha256 = Convert.ToHexStringLower(reader.ReadBytes(32));
            var issuedAtUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var now = _timeProvider.GetUtcNow();
            if (version != FormatVersion
                || afterCursor < 0
                || afterCursor > highWatermark
                || pageSize is < 1 or > AuthoritativeExportOptions.MaximumPageSize
                || highWatermark < 0
                || revisionHighWatermark < 0
                || expectedItemCount < 0
                || issuedAtUtc > now.AddMinutes(5)
                || now - issuedAtUtc > _lifetime)
            {
                throw InvalidCursor();
            }

            return new InteractionAuthoritativeExportCursorState(
                afterCursor,
                pageSize,
                new InteractionAuthoritativeExportSnapshot(
                    highWatermark,
                    revisionHighWatermark,
                    capturedAtUtc,
                    expectedItemCount,
                    minimumResourceVersion,
                    maximumResourceVersion,
                    minimumSummaryVersion,
                    maximumSummaryVersion,
                    globalXorSha256),
                issuedAtUtc);
        }
        catch (FormatException)
        {
            throw InvalidCursor();
        }
        catch (EndOfStreamException)
        {
            throw InvalidCursor();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidCursor();
        }
    }

    private static InteractionValidationException InvalidCursor() =>
        new("The authoritative export cursor is invalid or expired.");

    private static long? NullableVersion(long value) => value < 0 ? null : value;
}
