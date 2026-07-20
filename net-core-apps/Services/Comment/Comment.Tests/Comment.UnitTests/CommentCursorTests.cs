using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Queries;
using Comment.Domain.Repositories;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentCursorTests
{
    [Fact]
    public void Signed_cursor_round_trips_the_stable_date_and_id_position()
    {
        var clock = new StubTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        var codec = CreateCodec(clock, lifetimeSeconds: 900);
        var criteria = Criteria(
            ids: [9, 3, 9],
            targets:
            [
                new CommentTargetFilter { TargetId = "post-2", TargetType = "POST" },
                new CommentTargetFilter { TargetId = "post-1", TargetType = "post" },
            ]);
        var fingerprint = CommentCursorFingerprint.Create(criteria, "public");
        var expected = new CommentCursorPosition(
            new DateTime(638883504000000000, DateTimeKind.Utc),
            42);

        var encoded = codec.Encode(expected, fingerprint);
        var decoded = codec.TryDecode(encoded, fingerprint, out var actual);

        Assert.True(decoded);
        Assert.Equal(expected, actual);
        Assert.True(encoded.Length <= CommentCursorCodec.MaximumEncodedLength);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Cursor_is_bound_to_filter_scope_and_rejects_tampering()
    {
        var clock = new StubTimeProvider(DateTimeOffset.Parse("2026-07-17T12:00:00Z"));
        var codec = CreateCodec(clock, lifetimeSeconds: 900);
        var publicFingerprint = CommentCursorFingerprint.Create(
            Criteria(ids: [1, 2]),
            "public");
        var ownerFingerprint = CommentCursorFingerprint.Create(
            Criteria(ids: [1, 2]),
            "owner:7");
        var cursor = codec.Encode(
            new CommentCursorPosition(DateTime.UtcNow, 9),
            publicFingerprint);

        Assert.False(codec.TryDecode(cursor, ownerFingerprint, out _));

        var replacement = cursor[^1] == 'A' ? 'B' : 'A';
        var tampered = cursor[..^1] + replacement;
        Assert.False(codec.TryDecode(tampered, publicFingerprint, out _));
        Assert.False(codec.TryDecode("***", publicFingerprint, out _));
    }

    [Fact]
    public void Cursor_expires_and_a_future_issued_cursor_is_rejected()
    {
        var clock = new StubTimeProvider(DateTimeOffset.Parse("2026-07-17T12:00:00Z"));
        var codec = CreateCodec(clock, lifetimeSeconds: 300);
        var fingerprint = CommentCursorFingerprint.Create(new CommentsCriteria(), "public");
        var cursor = codec.Encode(
            new CommentCursorPosition(DateTime.UtcNow, 9),
            fingerprint);

        clock.UtcNow = clock.UtcNow.AddSeconds(301);
        Assert.False(codec.TryDecode(cursor, fingerprint, out _));

        var futureClock = new StubTimeProvider(DateTimeOffset.Parse("2026-07-17T11:54:59Z"));
        var futureCodec = CreateCodec(futureClock, lifetimeSeconds: 300);
        Assert.False(futureCodec.TryDecode(cursor, fingerprint, out _));
    }

    [Fact]
    public void Filter_fingerprint_is_order_independent_but_scope_sensitive()
    {
        var first = Criteria(
            ids: [7, 2, 7],
            targets:
            [
                new CommentTargetFilter { TargetId = "B", TargetType = "POST" },
                new CommentTargetFilter { TargetId = "A", TargetType = "post" },
            ]);
        var second = Criteria(
            ids: [2, 7],
            targets:
            [
                new CommentTargetFilter { TargetId = "A", TargetType = "post" },
                new CommentTargetFilter { TargetId = "B", TargetType = "post" },
            ]);

        Assert.Equal(
            CommentCursorFingerprint.Create(first, "public"),
            CommentCursorFingerprint.Create(second, "public"));
        Assert.NotEqual(
            CommentCursorFingerprint.Create(first, "public"),
            CommentCursorFingerprint.Create(first, "owner:1"));
    }

    [Fact]
    public void Cursor_options_reject_short_keys_and_unbounded_lifetimes()
    {
        Assert.False(CommentCursorOptions.IsValid(new CommentCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            LifetimeSeconds = 300,
        }));
        Assert.False(CommentCursorOptions.IsValid(new CommentCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            LifetimeSeconds = 604_801,
        }));
    }

    private static CommentsCriteria Criteria(
        int[]? ids = null,
        CommentTargetFilter[]? targets = null) => new()
        {
            Ids = ids,
            Targets = targets,
            DateRange =
        [
            DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-12-31T23:59:59Z").ToUniversalTime(),
        ],
        };

    private static CommentCursorCodec CreateCodec(
        TimeProvider clock,
        int lifetimeSeconds) => new(
        Options.Create(new CommentCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            LifetimeSeconds = lifetimeSeconds,
        }),
        clock);

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
