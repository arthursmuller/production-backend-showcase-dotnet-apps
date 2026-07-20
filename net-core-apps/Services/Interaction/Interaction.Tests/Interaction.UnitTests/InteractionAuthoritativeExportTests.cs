using Interaction.Application.Features.Exports;
using Interaction.Application.Features.Exports.Queries;
using Interaction.Application.Options;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Interaction.UnitTests;

public sealed class InteractionAuthoritativeExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Signed_keyset_snapshot_excludes_later_coordinates_and_never_duplicates()
    {
        var repository = new ExportRepository([Row(1, 1), Row(2, 2), Row(3, 3)]);
        var clock = new FixedTimeProvider(Now);
        var codec = Codec(clock);
        var query = new ExportInteractionAuthoritativeStateQuery(repository, codec, clock);

        var first = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 2),
            TestContext.Current.CancellationToken);
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(3, first.Snapshot.HighWatermark);
        Assert.Equal(3, first.Snapshot.ExpectedItemCount);
        Assert.All(first.Items, item => Assert.Equal(64, item.RowSha256.Length));

        repository.Rows.Add(Row(4, 4));
        repository.Rows[2] = Row(3, 9);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var second = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(first.NextCursor, null),
            TestContext.Current.CancellationToken);

        var exported = first.Items.Concat(second.Items).ToArray();
        Assert.Equal([1L, 2L, 3L], exported.Select(item => item.Cursor));
        Assert.Equal(exported.Length, exported.Select(item => item.Cursor).Distinct().Count());
        Assert.DoesNotContain(exported, item => item.Cursor == 4);
        Assert.Equal(3, Assert.Single(second.Items).EngagementSummary!.Version);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);
        Assert.Equal(
            second.Integrity.ItemCount,
            second.Integrity.Partitions.Sum(partition => partition.ItemCount));
        Assert.Equal(64, second.Integrity.OrderedPageSha256.Length);

        var fresh = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 500),
            TestContext.Current.CancellationToken);
        Assert.Equal(9, Assert.Single(fresh.Items, item => item.Cursor == 3).EngagementSummary!.Version);
        Assert.NotEqual(first.Snapshot.GlobalXorSha256, fresh.Snapshot.GlobalXorSha256);
    }

    [Fact]
    public async Task Export_rejects_oversized_pages_changed_windows_and_tampered_cursors()
    {
        var repository = new ExportRepository([Row(1, 1), Row(2, 2)]);
        var clock = new FixedTimeProvider(Now);
        var query = new ExportInteractionAuthoritativeStateQuery(repository, Codec(clock), clock);
        await Assert.ThrowsAsync<InteractionValidationException>(() => query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 501),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, repository.Captures);

        var first = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 1),
            TestContext.Current.CancellationToken);
        Assert.True(first.NextCursor!.Length <= InteractionAuthoritativeExportCursor.MaximumEncodedLength);
        await Assert.ThrowsAsync<InteractionValidationException>(() => query.Handle(
            new ExportInteractionAuthoritativeStateRequest(first.NextCursor, 2),
            TestContext.Current.CancellationToken));

        var tampered = first.NextCursor[..^1]
            + (first.NextCursor[^1] == 'A' ? "B" : "A");
        await Assert.ThrowsAsync<InteractionValidationException>(() => query.Handle(
            new ExportInteractionAuthoritativeStateRequest(tampered, null),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Continuation_cursor_keeps_the_original_absolute_expiry()
    {
        var repository = new ExportRepository([Row(1, 1), Row(2, 2), Row(3, 3), Row(4, 4)]);
        var clock = new FixedTimeProvider(Now);
        var query = new ExportInteractionAuthoritativeStateQuery(
            repository,
            Codec(clock, 60),
            clock);
        var first = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 1),
            TestContext.Current.CancellationToken);
        clock.UtcNow = Now.AddMinutes(30);
        var second = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(first.NextCursor, null),
            TestContext.Current.CancellationToken);
        clock.UtcNow = Now.AddMinutes(60).AddSeconds(1);

        await Assert.ThrowsAsync<InteractionValidationException>(() => query.Handle(
            new ExportInteractionAuthoritativeStateRequest(second.NextCursor, null),
            TestContext.Current.CancellationToken));
    }

    private static InteractionAuthoritativeExportCursor Codec(
        TimeProvider clock,
        int lifetimeMinutes = 360) =>
        new(Options.Create(new AuthoritativeExportOptions
        {
            CursorSigningKey = Convert.ToBase64String(
                Enumerable.Repeat((byte)0x42, 32).ToArray()),
            CursorLifetimeMinutes = lifetimeMinutes,
        }), clock);

    private static InteractionAuthoritativeExportRow Row(long cursor, long version) => new(
        cursor,
        ResourceType.Comment,
        $"comment-{cursor}",
        ResourceLifecycle.Active,
        ResourceVisibility.Public,
        version,
        version,
        false,
        version,
        Now,
        version,
        0,
        0,
        0,
        version * 10,
        0,
        0,
        0,
        0,
        false,
        true,
        false,
        version,
        Now,
        (byte)cursor,
        Hash(cursor, version));

    private static string Hash(long cursor, long version) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{cursor}:{version}")));

    private sealed class ExportRepository(IEnumerable<InteractionAuthoritativeExportRow> rows)
        : IInteractionAuthoritativeExportRepository
    {
        public List<InteractionAuthoritativeExportRow> Rows { get; } = [.. rows];
        public int Captures { get; private set; }
        private readonly Dictionary<long, InteractionAuthoritativeExportRow[]> _snapshots = [];

        public Task<InteractionAuthoritativeExportSnapshot> CaptureSnapshotAsync(
            CancellationToken cancellationToken)
        {
            Captures++;
            var snapshotRows = Rows.ToArray();
            var revisionHighWatermark = Captures;
            _snapshots.Add(revisionHighWatermark, snapshotRows);
            return Task.FromResult(new InteractionAuthoritativeExportSnapshot(
                snapshotRows.Select(item => item.Cursor).DefaultIfEmpty(0).Max(),
                revisionHighWatermark,
                Now,
                snapshotRows.LongLength,
                snapshotRows.Select(item => (long?)item.AppliedResourceVersion).Min(),
                snapshotRows.Select(item => (long?)item.AppliedResourceVersion).Max(),
                snapshotRows.Select(item => item.SummaryVersion).Min(),
                snapshotRows.Select(item => item.SummaryVersion).Max(),
                Xor(snapshotRows.Select(item => item.RowSha256))));
        }

        public Task<InteractionAuthoritativeExportSlice> ReadPageAsync(
            long afterCursor,
            long snapshotHighWatermark,
            long snapshotRevisionHighWatermark,
            int take,
            CancellationToken cancellationToken)
        {
            var candidates = _snapshots[snapshotRevisionHighWatermark]
                .Where(item => item.Cursor > afterCursor && item.Cursor <= snapshotHighWatermark)
                .OrderBy(item => item.Cursor)
                .Take(take + 1)
                .ToArray();
            return Task.FromResult(new InteractionAuthoritativeExportSlice(
                candidates.Take(take).ToArray(),
                candidates.Length > take));
        }

        private static string Xor(IEnumerable<string> hashes)
        {
            var result = new byte[32];
            foreach (var hash in hashes)
            {
                var bytes = Convert.FromHexString(hash);
                for (var index = 0; index < result.Length; index++)
                    result[index] ^= bytes[index];
            }
            return Convert.ToHexStringLower(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
