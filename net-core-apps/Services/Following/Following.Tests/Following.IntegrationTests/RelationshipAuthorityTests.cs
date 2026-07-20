using System.Reflection;
using Following.Domain.AggregatesModel;
using Following.Infra.Migrations;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Following.UnitTests;

public sealed class RelationshipAuthorityTests
{
    [Fact]
    public void Durable_stream_version_never_resets_across_revoke_and_recreate()
    {
        var streamId = Guid.NewGuid();
        var stream = RelationshipEventStream.Create(
            streamId,
            Guid.NewGuid(),
            ownerUserId: 10,
            viewerUserId: 20,
            relationshipKind: "follow",
            state: "active",
            occurredAtUtc: Utc(0));

        Assert.Equal(1, stream.CurrentVersion);
        Assert.Equal(2, stream.Advance(Guid.NewGuid(), "revoked", Utc(1)));
        Assert.Equal(3, stream.Advance(Guid.NewGuid(), "active", Utc(2)));
        Assert.Equal(streamId, stream.StreamId);
        Assert.Equal(3, stream.CurrentVersion);
        Assert.Equal("active", stream.CurrentState);
        Assert.Equal(3, stream.Version);
    }

    [Fact]
    public void Relationship_stream_model_has_durable_identity_and_optimistic_concurrency()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(RelationshipEventStream)));

        Assert.Equal("RelationshipEventStream", entity.GetTableName());
        Assert.True(entity.FindProperty(nameof(RelationshipEventStream.Version))!.IsConcurrencyToken);
        Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "UX_RelationshipEventStream_StreamId"
                && index.IsUnique);
        Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "UX_RelationshipEventStream_Direction_Kind"
                && index.IsUnique);
    }

    [Fact]
    public void Authority_migration_is_additive_and_bootstraps_existing_relationships()
    {
        var operations = BuildOperations("Up");
        Assert.All(
            operations,
            operation => Assert.True(
                operation is CreateTableOperation or CreateIndexOperation or SqlOperation,
                $"Unexpected operation: {operation.GetType().Name}"));
        Assert.Contains(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "RelationshipEventStream");
        var bootstrap = Assert.Single(operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("FROM `Follow`", bootstrap, StringComparison.Ordinal);
        Assert.Contains("FROM `Friendship`", bootstrap, StringComparison.Ordinal);
        Assert.Contains("'follow', 'active', 1", bootstrap, StringComparison.Ordinal);
        Assert.Contains("'friend', 'active', 1", bootstrap, StringComparison.Ordinal);

        var rollback = BuildOperations("Down");
        var drop = Assert.Single(rollback);
        Assert.Equal("RelationshipEventStream", Assert.IsType<DropTableOperation>(drop).Name);
    }

    [Fact]
    public void User_block_model_and_migration_enforce_directional_authority()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(UserBlock)));

        Assert.Equal("UserBlock", entity.GetTableName());
        Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "UX_UserBlock_Direction"
                && index.IsUnique);
        Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "IX_UserBlock_ReverseDirection"
                && !index.IsUnique);

        var migration = new AddAuthoritativeUserBlocks();
        var up = BuildOperations(migration, "Up");
        Assert.All(
            up,
            operation => Assert.True(
                operation is CreateTableOperation or CreateIndexOperation or SqlOperation,
                $"Unexpected operation: {operation.GetType().Name}"));
        Assert.Contains(
            up.OfType<CreateTableOperation>(),
            operation => operation.Name == "UserBlock");
        var guard = Assert.Single(
            up.OfType<CreateTableOperation>(),
            operation => operation.Name == "RelationshipPairGuard");
        Assert.Equal(
            ["PairMinUserId", "PairMaxUserId"],
            guard.PrimaryKey!.Columns);
        Assert.Contains(
            up.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_UserBlock_Blocker_Cursor");
        var triggerSql = up.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        Assert.Equal(3, triggerSql.Length);
        Assert.All(triggerSql, sql =>
        {
            Assert.Contains("RejectBlockedInsert", sql, StringComparison.Ordinal);
            Assert.Contains("SIGNAL SQLSTATE '45000'", sql, StringComparison.Ordinal);
        });

        var down = BuildOperations(migration, "Down");
        Assert.Equal(3, down.OfType<SqlOperation>().Count());
        Assert.Equal(
            ["RelationshipPairGuard", "UserBlock"],
            down.OfType<DropTableOperation>()
                .Select(operation => operation.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static FollowingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                "Server=localhost;Database=following_authority_model_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new FollowingContext(options);
    }

    private static List<MigrationOperation> BuildOperations(string methodName)
        => BuildOperations(new AuthoritativeRelationshipStreams(), methodName);

    private static List<MigrationOperation> BuildOperations(
        Migration migration,
        string methodName)
    {
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = migration.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        return builder.Operations.ToList();
    }

    private static DateTime Utc(int minutes) =>
        new DateTime(2026, 7, 17, 12, minutes, 0, DateTimeKind.Utc);
}
