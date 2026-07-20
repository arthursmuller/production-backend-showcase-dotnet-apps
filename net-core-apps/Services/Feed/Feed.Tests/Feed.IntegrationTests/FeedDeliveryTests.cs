using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Database.Ef.Migrations;
using Feed.Infra.Persistence;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedDeliveryTests
{
    private static readonly DateTime Now =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Publisher_mode_uses_100k_80k_hysteresis_without_flapping()
    {
        var state = FeedPublisherState.Create(7, 99_999, 100_000, 80_000, Now);

        Assert.Equal(FeedDeliveryModes.Push, state.DeliveryMode);
        state.ObserveAudienceCount(100_000, 100_000, 80_000, Now.AddMinutes(1));
        Assert.Equal(FeedDeliveryModes.Pull, state.DeliveryMode);
        state.ObserveAudienceCount(90_000, 100_000, 80_000, Now.AddMinutes(2));
        Assert.Equal(FeedDeliveryModes.Pull, state.DeliveryMode);
        state.ObserveAudienceCount(80_000, 100_000, 80_000, Now.AddMinutes(3));
        Assert.Equal(FeedDeliveryModes.Push, state.DeliveryMode);
    }

    [Fact]
    public void Fanout_checkpoint_is_monotonic_and_rejects_a_stale_lease()
    {
        var activity = Activity();
        var work = FanoutWorkItem.Create("event:1", activity, Now);
        var firstLease = work.Lease("worker-a", Now, TimeSpan.FromSeconds(10));
        work.InitializeAudienceSnapshot(firstLease, 1_200, Now);
        work.Advance(firstLease, 500, completed: false, Now.AddSeconds(1));
        var secondLease = work.Lease("worker-b", Now.AddSeconds(2), TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() =>
            work.Advance(firstLease, 600, completed: false, Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() =>
            work.Advance(secondLease, 499, completed: false, Now.AddSeconds(3)));
        work.Advance(secondLease, 1_000, completed: false, Now.AddSeconds(3));

        Assert.Equal(1_000, work.NextViewerUserId);
        Assert.Equal(1_200, work.AudienceHighWatermarkUserId);
        Assert.Equal(FeedWorkStates.Pending, work.State);
    }

    [Fact]
    public void Revocation_work_preserves_authoritative_source_time_separately_from_creation()
    {
        var activity = Activity();
        var sourceOccurredAtUtc = Now.AddSeconds(-4);

        var work = FeedRevocationWorkItem.ForActivity(
            "activity:event-1",
            activity,
            sourceOccurredAtUtc,
            Now);

        Assert.Equal(sourceOccurredAtUtc, work.SourceOccurredAtUtc);
        Assert.Equal(Now, work.CreatedAtUtc);
    }

    [Fact]
    public void Cursor_round_trip_preserves_snapshot_and_full_deterministic_tuple()
    {
        var cursor = new FeedCursor(
            Now.Ticks,
            new FeedSortTuple(300, Now.Ticks, 90),
            new FeedSortTuple(200, Now.AddSeconds(-1).Ticks, 80));
        var cursorOptions = new FeedCursorOptions
        {
            ActiveKeyId = "primary",
            SigningKeys = new Dictionary<string, string>
            {
                ["primary"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 32).ToArray()),
            },
        };

        var encoded = FeedCursorCodec.Encode(
            cursor, 17, 9, 20, true, FeedCursorSurfaces.Items,
            cursorOptions, new DateTimeOffset(Now), 512);
        var decoded = FeedCursorCodec.Decode(
            encoded, 17, 9, 20, true, FeedCursorSurfaces.Items,
            cursorOptions, new DateTimeOffset(Now), 512);

        Assert.Equal(cursor, decoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Throws<FeedCursorRejectedException>(() => FeedCursorCodec.Decode(
            encoded + "!", 17, 9, 20, true, FeedCursorSurfaces.Items,
            cursorOptions, new DateTimeOffset(Now), 512));
    }

    [Fact]
    public void Feed_options_enforce_all_production_hard_caps()
    {
        Assert.True(FeedOptionsValidation.IsValid(new FeedOptions()));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumPullPublishers = 65,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumCandidates = 513,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumPageSize = 51,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            PullExitAudienceCount = 100_000,
        }));
    }

    [Fact]
    public void Delivery_migration_is_expand_only_and_backfills_before_positive_checks()
    {
        var migration = new FeedDelivery();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(FeedDelivery).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        var operations = builder.Operations.ToList();
        var backfill = operations.FindIndex(operation =>
            operation is SqlOperation sql
            && sql.Sql.Contains("PublisherUserId", StringComparison.Ordinal)
            && sql.Sql.Contains("SourceStreamId", StringComparison.Ordinal));
        var publisherCheck = operations.FindIndex(operation =>
            operation is AddCheckConstraintOperation check
            && check.Name == "CK_FanoutWorkItem_Publisher_Positive");

        Assert.True(backfill >= 0);
        Assert.True(publisherCheck > backfill);
        Assert.DoesNotContain(operations, operation =>
            operation is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void Revocation_source_time_migration_expands_backfills_then_contracts()
    {
        using var context = CreateContext();
        Assert.Contains(
            "20260721000000_FeedRevocationSourceOccurrence",
            context.Database.GetMigrations());

        var migration = new FeedRevocationSourceOccurrence();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(FeedRevocationSourceOccurrence).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        var operations = builder.Operations.ToList();

        var expand = operations.FindIndex(operation =>
            operation is AddColumnOperation add
            && add.Table == "FeedRevocationWorkItem"
            && add.Name == "SourceOccurredAtUtc"
            && add.IsNullable);
        var backfill = operations.FindIndex(operation =>
            operation is SqlOperation sql
            && sql.Sql.Contains("SourceOccurredAtUtc", StringComparison.Ordinal)
            && sql.Sql.Contains("CreatedAtUtc", StringComparison.Ordinal));
        var contract = operations.FindIndex(operation =>
            operation is AlterColumnOperation alter
            && alter.Table == "FeedRevocationWorkItem"
            && alter.Name == "SourceOccurredAtUtc"
            && !alter.IsNullable
            && alter.OldColumn.IsNullable);

        Assert.True(expand >= 0);
        Assert.True(backfill > expand);
        Assert.True(contract > backfill);
        Assert.DoesNotContain(operations, operation =>
            operation is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void Delivery_model_has_dedupe_priority_fairness_and_pull_cursor_indexes()
    {
        using var context = CreateContext();

        AssertIndex(context, typeof(FeedActivity), "UX_FeedActivity_SourceStreamId", unique: true);
        AssertIndex(context, typeof(FeedActivity), "IX_FeedActivity_Pull_Cursor", unique: false);
        AssertIndex(context, typeof(FanoutWorkItem), "IX_FanoutWorkItem_FairClaim", unique: false);
        AssertIndex(context, typeof(FeedRevocationWorkItem), "UX_FeedRevocationWorkItem_SourceEventId", unique: true);
        AssertIndex(context, typeof(FeedRevocationWorkItem), "IX_FeedRevocationWorkItem_PriorityClaim", unique: false);
        var sourceOccurred = context.Model.FindEntityType(typeof(FeedRevocationWorkItem))?
            .FindProperty(nameof(FeedRevocationWorkItem.SourceOccurredAtUtc));
        Assert.NotNull(sourceOccurred);
        Assert.False(sourceOccurred!.IsNullable);
        Assert.Equal("datetime(6)", sourceOccurred.GetColumnType());
    }

    private static FeedActivity Activity() => FeedActivity.Create(
        "post",
        "42",
        "created",
        actorUserId: 7,
        FeedAudiencePolicies.Followers,
        Now,
        Now,
        sourceStreamId: Guid.NewGuid(),
        eventId: Guid.NewGuid());

    private static FeedContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                "Server=localhost;Database=feed_delivery_model;User=root;Password=not-used;",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        return new FeedContext(options);
    }

    private static void AssertIndex(
        FeedContext context,
        Type entity,
        string name,
        bool unique)
    {
        var index = context.Model.FindEntityType(entity)?.GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.Equal(unique, index!.IsUnique);
    }
}
