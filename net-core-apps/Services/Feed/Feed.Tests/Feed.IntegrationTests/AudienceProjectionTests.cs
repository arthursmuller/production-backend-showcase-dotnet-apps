using System.Reflection;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Migrations;
using Feed.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MessageBrokerContracts.Following;
using Xunit;

namespace Feed.UnitTests;

public sealed class AudienceProjectionTests
{
    [Fact]
    public void Checkpoint_applies_only_next_versions_and_fails_closed_during_a_gap()
    {
        var streamId = Guid.NewGuid();
        var checkpoint = FeedRelationshipCheckpoint.Create(
            streamId,
            ownerUserId: 10,
            viewerUserId: 20,
            relationshipKind: "follow",
            observedAtUtc: Utc(0));

        Assert.Equal(
            ProjectionVersionDisposition.Gap,
            checkpoint.Classify(streamId, 10, 20, "follow", incomingVersion: 2));
        checkpoint.MarkGap(2, Utc(1));
        Assert.Equal(AudienceProjectionStates.Gap, checkpoint.Status);
        Assert.False(checkpoint.IsFresh(Utc(2), TimeSpan.FromHours(1)));

        var reconciledEventId = Guid.NewGuid();
        checkpoint.Reconcile(reconciledEventId, 2, Utc(1), Utc(2));
        Assert.Equal(AudienceProjectionStates.Healthy, checkpoint.Status);
        Assert.Equal(2, checkpoint.LastAppliedVersion);
        Assert.Equal(reconciledEventId, checkpoint.LastEventId);
        Assert.Equal(
            ProjectionVersionDisposition.StaleOrDuplicate,
            checkpoint.Classify(streamId, 10, 20, "follow", incomingVersion: 2));
        Assert.Equal(
            ProjectionVersionDisposition.Next,
            checkpoint.Classify(streamId, 10, 20, "follow", incomingVersion: 3));
        Assert.True(checkpoint.IsFresh(Utc(3), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Audience_edge_enforces_stream_identity_and_contiguous_versions()
    {
        var streamId = Guid.NewGuid();
        var edge = FeedAudienceEdge.Create(
            10,
            20,
            "friend",
            "active",
            streamId,
            Guid.NewGuid(),
            1,
            Utc(0),
            Utc(0));

        edge.ApplyNext(streamId, Guid.NewGuid(), "revoked", 2, Utc(1), Utc(1));
        Assert.Equal(2, edge.SourceVersion);
        Assert.Equal("revoked", edge.State);
        Assert.Throws<InvalidOperationException>(() =>
            edge.ApplyNext(streamId, Guid.NewGuid(), "active", 4, Utc(2), Utc(2)));
        Assert.Throws<InvalidOperationException>(() =>
            edge.ApplyNext(Guid.NewGuid(), Guid.NewGuid(), "active", 3, Utc(2), Utc(2)));
    }

    [Fact]
    public void Projection_model_has_checkpoint_quarantine_and_stream_identity_constraints()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var edge = AssertEntity<FeedAudienceEdge>(model);
        var checkpoint = AssertEntity<FeedRelationshipCheckpoint>(model);
        var quarantine = AssertEntity<FeedRelationshipEventQuarantine>(model);
        var bootstrap = AssertEntity<FeedAudienceBootstrapState>(model);

        Assert.Single(
            edge.GetIndexes(),
            index => index.GetDatabaseName() == "UX_FeedAudienceEdge_SourceStreamId"
                && index.IsUnique);
        Assert.Single(
            checkpoint.GetIndexes(),
            index => index.GetDatabaseName() == "UX_FeedRelationshipCheckpoint_Direction_Kind"
                && index.IsUnique);
        Assert.True(checkpoint.FindProperty(nameof(FeedRelationshipCheckpoint.Version))!.IsConcurrencyToken);
        Assert.Single(
            quarantine.GetIndexes(),
            index => index.GetDatabaseName() == "UX_FeedRelationshipEventQuarantine_Stream_Version"
                && index.IsUnique);
        Assert.Equal(
            ValueGenerated.Never,
            bootstrap.FindPrimaryKey()!.Properties.Single().ValueGenerated);
        Assert.True(bootstrap.FindProperty(nameof(FeedAudienceBootstrapState.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void Bootstrap_state_requires_one_fixed_complete_fresh_cycle_and_degrades_fail_closed()
    {
        var state = FeedAudienceBootstrapState.Create(Utc(0));
        Assert.False(state.IsCompleteAndFresh(Utc(1), TimeSpan.FromHours(1)));

        var firstLease = Assert.IsType<Guid>(state.TryLease(
            "worker-a",
            Utc(0),
            TimeSpan.FromMinutes(2)));
        Assert.Null(state.TryLease("worker-b", Utc(0), TimeSpan.FromMinutes(2)));
        state.BeginCycle(firstLease, Utc(0));
        state.ApplyPage(
            firstLease,
            snapshotHighWatermark: 10,
            snapshotAsOfUtc: DateTime.UnixEpoch,
            nextCursor: 5,
            hasMore: true,
            observedAtUtc: Utc(1));

        Assert.True(state.CycleInProgress);
        Assert.Equal(5, state.NextCursor);
        Assert.Equal(10, state.SnapshotHighWatermark);
        var finalLease = Assert.IsType<Guid>(state.TryLease(
            "worker-b",
            Utc(1),
            TimeSpan.FromMinutes(2)));
        state.BeginCycle(finalLease, Utc(1));
        state.ApplyPage(
            finalLease,
            snapshotHighWatermark: 10,
            snapshotAsOfUtc: DateTime.UnixEpoch,
            nextCursor: 10,
            hasMore: false,
            observedAtUtc: Utc(2));

        Assert.Equal(AudienceBootstrapStates.Healthy, state.Status);
        Assert.False(state.CycleInProgress);
        Assert.Equal(DateTime.UnixEpoch, state.SnapshotAsOfUtc);
        Assert.True(state.IsCompleteAndFresh(Utc(3), TimeSpan.FromHours(1)));
        Assert.False(state.IsCompleteAndFresh(Utc(5), TimeSpan.FromMinutes(2)));

        state.MarkDegraded("relationship_version_gap", Utc(3));
        Assert.Equal(AudienceBootstrapStates.Degraded, state.Status);
        Assert.False(state.IsCompleteAndFresh(Utc(3), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Bootstrap_page_validation_rejects_skipped_cursor_ranges_and_oversized_pages()
    {
        var validator = Assert.IsAssignableFrom<MethodInfo>(
            typeof(RelationshipBootstrapProcessor).GetMethod(
                "ValidatePage",
                BindingFlags.Static | BindingFlags.NonPublic));
        var skippedRange = new RelationshipSnapshotPage
        {
            Items = [],
            NextCursor = 10,
            HasMore = false,
            SnapshotHighWatermark = 10,
            SnapshotAsOfUtc = DateTime.UnixEpoch,
        };
        AssertValidationFailure(validator, skippedRange, requestedPageSize: 500);

        var oversized = new RelationshipSnapshotPage
        {
            Items = [Snapshot(1), Snapshot(2)],
            NextCursor = 2,
            HasMore = false,
            SnapshotHighWatermark = 2,
            SnapshotAsOfUtc = DateTime.UnixEpoch,
        };
        AssertValidationFailure(validator, oversized, requestedPageSize: 1);
    }

    [Fact]
    public void Projection_migration_is_expand_only()
    {
        var operations = BuildOperations("Up");
        Assert.All(
            operations,
            operation => Assert.True(
                operation is AddColumnOperation or CreateTableOperation or CreateIndexOperation,
                $"Unexpected operation: {operation.GetType().Name}"));
        Assert.Equal(3, operations.OfType<AddColumnOperation>().Count());
        Assert.Equal(
            ["FeedRelationshipCheckpoint", "FeedRelationshipEventQuarantine"],
            operations.OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Local_projection_store_has_no_synchronous_following_provider_dependency()
    {
        var constructor = Assert.Single(typeof(AudienceProjectionStore).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType.Namespace?.Contains(
                "FollowingProvider",
                StringComparison.Ordinal) == true);
    }

    private static FeedContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                "Server=localhost;Database=feed_audience_model_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new FeedContext(options);
    }

    private static IEntityType AssertEntity<TEntity>(IModel model) where TEntity : class =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(TEntity)));

    private static void AssertValidationFailure(
        MethodInfo validator,
        RelationshipSnapshotPage page,
        int requestedPageSize)
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            validator.Invoke(null, [page, 0L, null, requestedPageSize]));
        Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerException);
    }

    private static RelationshipStateSnapshot Snapshot(long cursor) => new()
    {
        Cursor = cursor,
        StreamId = Guid.NewGuid(),
        LastEventId = Guid.NewGuid(),
        StreamVersion = 1,
        OwnerUserId = 10,
        ViewerUserId = 20,
        RelationshipKind = RelationshipKinds.Friend,
        State = RelationshipStates.Active,
        AsOfUtc = Utc(0),
    };

    private static List<MigrationOperation> BuildOperations(string methodName)
    {
        var migration = new AudienceProjection();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(AudienceProjection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        return builder.Operations.ToList();
    }

    private static DateTime Utc(int minutes) =>
        new DateTime(2026, 7, 17, 12, minutes, 0, DateTimeKind.Utc);
}
