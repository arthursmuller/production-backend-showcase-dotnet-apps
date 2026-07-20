using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Exports;
using Moderation.Application.Features.Exports.Queries;
using Moderation.Application.Options;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;
using System.Security.Cryptography;
using System.Text;

namespace Moderation.UnitTests;

public sealed class ModerationDecisionExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Signed_keyset_snapshot_excludes_later_decisions_and_never_duplicates()
    {
        var repository = new ExportRepository([Row(1, 1, 6), Row(2, 1, 7), Row(3, 1, 8)]);
        var clock = new FixedTimeProvider(Now);
        var query = new ExportModerationDecisionStateQuery(repository, Codec(clock), clock);

        var firstResult = await query.HandleAsync(null, 2, TestContext.Current.CancellationToken);
        Assert.True(firstResult.IsSuccess);
        var first = firstResult.Value!;
        Assert.True(first.HasMore);
        Assert.Equal(3, first.Snapshot.HighWatermark);
        Assert.Equal(3, first.Snapshot.ExpectedItemCount);

        repository.Rows.Add(Row(4, 1, 9));
        repository.Rows[2] = Row(3, 2, 10);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var secondResult = await query.HandleAsync(
            first.NextCursor,
            null,
            TestContext.Current.CancellationToken);
        Assert.True(secondResult.IsSuccess);
        var second = secondResult.Value!;

        var exported = first.Items.Concat(second.Items).ToArray();
        Assert.Equal([1L, 2L, 3L], exported.Select(item => item.Cursor));
        Assert.Equal(exported.Length, exported.Select(item => item.Cursor).Distinct().Count());
        Assert.DoesNotContain(exported, item => item.Cursor == 4);
        Assert.Equal(1, Assert.Single(second.Items).DecisionVersion);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.False(second.HasMore);
        Assert.All(exported, item => Assert.Equal(64, item.RowSha256.Length));
        Assert.Equal(
            second.Integrity.ItemCount,
            second.Integrity.Partitions.Sum(partition => partition.ItemCount));

        var fresh = (await query.HandleAsync(
            null,
            500,
            TestContext.Current.CancellationToken)).Value!;
        Assert.Equal(2, Assert.Single(fresh.Items, item => item.Cursor == 3).DecisionVersion);
        Assert.NotEqual(first.Snapshot.GlobalXorSha256, fresh.Snapshot.GlobalXorSha256);
    }

    [Fact]
    public async Task Export_fails_closed_for_bounds_changed_windows_and_tampered_cursors()
    {
        var repository = new ExportRepository([Row(1, 1, 6), Row(2, 1, 7)]);
        var clock = new FixedTimeProvider(Now);
        var query = new ExportModerationDecisionStateQuery(repository, Codec(clock), clock);

        var oversized = await query.HandleAsync(null, 501, TestContext.Current.CancellationToken);
        Assert.False(oversized.IsSuccess);
        Assert.Equal(0, repository.Captures);

        var first = (await query.HandleAsync(null, 1, TestContext.Current.CancellationToken)).Value!;
        Assert.True(first.NextCursor!.Length <= ModerationDecisionExportCursor.MaximumEncodedLength);
        Assert.False((await query.HandleAsync(
            first.NextCursor,
            2,
            TestContext.Current.CancellationToken)).IsSuccess);
        var tampered = first.NextCursor[..^1]
            + (first.NextCursor[^1] == 'A' ? "B" : "A");
        Assert.False((await query.HandleAsync(
            tampered,
            null,
            TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task Continuation_cursor_keeps_the_original_absolute_expiry()
    {
        var repository = new ExportRepository(
            [Row(1, 1, 6), Row(2, 1, 7), Row(3, 1, 8), Row(4, 1, 9)]);
        var clock = new FixedTimeProvider(Now);
        var query = new ExportModerationDecisionStateQuery(
            repository,
            Codec(clock, 60),
            clock);
        var first = (await query.HandleAsync(
            null,
            1,
            TestContext.Current.CancellationToken)).Value!;
        clock.UtcNow = Now.AddMinutes(30);
        var second = (await query.HandleAsync(
            first.NextCursor,
            null,
            TestContext.Current.CancellationToken)).Value!;
        clock.UtcNow = Now.AddMinutes(60).AddSeconds(1);

        Assert.False((await query.HandleAsync(
            second.NextCursor,
            null,
            TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public void Restricted_decision_authority_shapes_cannot_carry_sensitive_case_material()
    {
        var allowedDomain = new[]
        {
            "ExportCursor", "CaseId", "ResourceKind", "ResourceId", "Action",
            "DecisionVersion", "CaseVersion", "DecidedAtUtc",
        };
        Assert.Equal(
            allowedDomain.Order(),
            typeof(ModerationDecisionState).GetProperties().Select(property => property.Name).Order());

        var prohibited = new[]
        {
            "report", "evidence", "reason", "message", "actor", "assignment",
            "investigator", "audit", "legalhold", "content", "reference",
        };
        var exportedProperties = typeof(ModerationDecisionExportItemDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        foreach (var property in exportedProperties)
            foreach (var fragment in prohibited)
                Assert.DoesNotContain(fragment, property, StringComparison.OrdinalIgnoreCase);
    }

    private static ModerationDecisionExportCursor Codec(
        TimeProvider clock,
        int lifetimeMinutes = 360) =>
        new(Options.Create(new ModerationDecisionExportOptions
        {
            CursorSigningKey = Convert.ToBase64String(
                Enumerable.Repeat((byte)0x24, 32).ToArray()),
            CursorLifetimeMinutes = lifetimeMinutes,
        }), clock);

    private static ModerationDecisionExportRow Row(
        long cursor,
        long decisionVersion,
        long caseVersion) => new(
        cursor,
        Guid.Parse($"00000000-0000-0000-0000-{cursor:D12}"),
        ResourceKind.Comment,
        $"comment-{cursor}",
        DecisionAction.RemoveContent,
        decisionVersion,
        caseVersion,
        Now,
        (byte)cursor,
        Hash(cursor, decisionVersion, caseVersion));

    private static string Hash(long cursor, long decisionVersion, long caseVersion) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{cursor}:{decisionVersion}:{caseVersion}")));

    private sealed class ExportRepository(IEnumerable<ModerationDecisionExportRow> rows)
        : IModerationDecisionExportRepository
    {
        public List<ModerationDecisionExportRow> Rows { get; } = [.. rows];
        public int Captures { get; private set; }
        private readonly Dictionary<long, ModerationDecisionExportRow[]> _snapshots = [];

        public Task<ModerationDecisionExportSnapshot> CaptureSnapshotAsync(
            CancellationToken cancellationToken)
        {
            Captures++;
            var snapshotRows = Rows.ToArray();
            var revisionHighWatermark = Captures;
            _snapshots.Add(revisionHighWatermark, snapshotRows);
            return Task.FromResult(new ModerationDecisionExportSnapshot(
                snapshotRows.Select(item => item.Cursor).DefaultIfEmpty(0).Max(),
                revisionHighWatermark,
                Now,
                snapshotRows.LongLength,
                snapshotRows.Select(item => (long?)item.DecisionVersion).Min(),
                snapshotRows.Select(item => (long?)item.DecisionVersion).Max(),
                snapshotRows.Select(item => (long?)item.CaseVersion).Min(),
                snapshotRows.Select(item => (long?)item.CaseVersion).Max(),
                Xor(snapshotRows.Select(item => item.RowSha256))));
        }

        public Task<ModerationDecisionExportSlice> ReadPageAsync(
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
            return Task.FromResult(new ModerationDecisionExportSlice(
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
