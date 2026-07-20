using Following.API.AppInterfaces.Queues;
using Following.Application.Providers.Privacy;
using Following.Domain.AggregatesModel;
using Following.Infra.Database.Ef.Migrations;
using Following.Infra.Persistence;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingUserErasureTests
{
    [Fact]
    public void Tombstone_is_permanent_monotonic_and_fenced()
    {
        var now = Utc(0);
        var firstRequest = Guid.NewGuid();
        var tombstone = FollowingUserErasure.Create(42, firstRequest, 1, now, now);

        tombstone.Claim("worker-a", now.AddSeconds(30), now);
        var token = tombstone.LeaseToken;
        Assert.True(tombstone.OwnsLease("worker-a", token, now));
        Assert.Throws<InvalidOperationException>(() => tombstone.Advance(
            FollowingUserErasureStage.FollowsAsFollower,
            10,
            "worker-b",
            token,
            now.AddSeconds(30),
            now));

        var newerRequest = Guid.NewGuid();
        tombstone.ScheduleNewer(newerRequest, 2, now.AddMinutes(1), now.AddMinutes(1));
        Assert.Equal(newerRequest, tombstone.RequestId);
        Assert.Equal(2, tombstone.ErasureVersion);
        Assert.Equal(FollowingUserErasureStatus.Pending, tombstone.Status);
        Assert.Equal(FollowingUserErasureStage.FollowsAsFollower, tombstone.Stage);
        Assert.False(tombstone.OwnsLease("worker-a", token, now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => tombstone.ScheduleNewer(
            Guid.NewGuid(), 2, now, now));
    }

    [Fact]
    public void Analytics_shards_are_stable_bounded_and_well_distributed()
    {
        var shards = Enumerable.Range(1, 10_000)
            .Select(UserRelationAnalytics.ResolveShard)
            .ToHashSet();

        Assert.Equal(UserRelationAnalytics.ShardCount, shards.Count);
        Assert.All(shards, shard => Assert.InRange(
            shard,
            (byte)0,
            (byte)(UserRelationAnalytics.ShardCount - 1)));
        Assert.Equal(
            UserRelationAnalytics.ResolveShard(12345),
            UserRelationAnalytics.ResolveShard(12345));
    }

    [Fact]
    public void Erasure_options_enforce_hard_budgets_and_lease_margin()
    {
        Assert.True(FollowingUserErasureOptions.IsValid(new FollowingUserErasureOptions()));
        Assert.False(FollowingUserErasureOptions.IsValid(new FollowingUserErasureOptions
        {
            BatchSize = 201,
        }));
        Assert.False(FollowingUserErasureOptions.IsValid(new FollowingUserErasureOptions
        {
            CycleTimeBudgetMilliseconds = 5_000,
            LeaseSeconds = 5,
        }));
    }

    [Fact]
    public void Restricted_consumer_maps_only_the_bounded_schedule_coordinate()
    {
        var request = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = 72,
            ErasureVersion = 3,
            RequestedAtUtc = new DateTimeOffset(Utc(0)),
        };

        var mapped = UserErasureRequestedConsumer.Map(request);

        Assert.Equal(request.EventId, mapped.EventId);
        Assert.Equal(request.RequestId, mapped.RequestId);
        Assert.Equal(72, mapped.UserId);
        Assert.Equal(3, mapped.ErasureVersion);
        Assert.Throws<InvalidOperationException>(() => UserErasureRequestedConsumer.Map(
            request with { UserId = (long)int.MaxValue + 1 }));
    }

    [Fact]
    public void Persistence_model_uses_composite_shards_tombstone_filters_and_fencing()
    {
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                "Server=localhost;Database=model;User=model;Password=model",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        using var context = new FollowingContext(options);
        var analytics = context.Model.FindEntityType(typeof(UserRelationAnalytics))!;
        Assert.Equal(
            [nameof(UserRelationAnalytics.Id), nameof(UserRelationAnalytics.ShardId)],
            analytics.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.NotEmpty(analytics.GetDeclaredQueryFilters());

        var erasure = context.Model.FindEntityType(typeof(FollowingUserErasure))!;
        Assert.True(erasure.FindProperty(nameof(FollowingUserErasure.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(erasure.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_FollowingUserErasure_Claim");

        foreach (var type in new[]
        {
            typeof(Follow), typeof(Friendship), typeof(FriendRequest), typeof(UserBlock),
            typeof(FriendshipSubscriptionFeature), typeof(CategoryFollow),
            typeof(RelationshipEventStream),
        })
        {
            Assert.NotEmpty(context.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
        }
    }

    [Fact]
    public void Migrations_install_erasure_guards_indexes_and_drop_the_retired_analytics_table()
    {
        var authority = Operations(new AddFollowingUserErasureAndAnalyticsShards());
        Assert.Contains(authority.OfType<CreateTableOperation>(), operation =>
            operation.Name == "FollowingUserErasure");
        Assert.Contains(authority.OfType<CreateTableOperation>(), operation =>
            operation.Name == "UserMutationGuard");
        var sql = string.Join('\n', authority.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("RejectErasedInsert", sql, StringComparison.Ordinal);
        Assert.Contains("RejectErasedUpdate", sql, StringComparison.Ordinal);
        Assert.Contains("TR_FollowingUserErasure_RowVersion", sql, StringComparison.Ordinal);

        var retirementSql = string.Join(
            '\n',
            Operations(new DropRetiredAnalyticsTable())
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));
        Assert.Contains(
            "DROP TABLE IF EXISTS `UserRelationAnalyticsLegacy`",
            retirementSql,
            StringComparison.Ordinal);

        var indexes = Operations(new AddFollowingErasureKeysetIndexes())
            .OfType<CreateIndexOperation>()
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(13, indexes.Count);
        Assert.Contains("IX_Follow_Erase_Follower", indexes);
        Assert.Contains("IX_RelStream_Erase_Viewer", indexes);
    }

    private static IReadOnlyList<MigrationOperation> Operations(Migration migration)
    {
        var builder = new MigrationBuilder("MySql");
        var method = migration.GetType().GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static DateTime Utc(int minute) =>
        new(2026, 7, 17, 12, minute, 0, DateTimeKind.Utc);
}
