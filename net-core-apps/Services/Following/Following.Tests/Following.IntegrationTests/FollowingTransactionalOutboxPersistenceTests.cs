using System.Reflection;
using Following.Infra.Migrations;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingTransactionalOutboxPersistenceTests
{
    private const string MigrationId =
        "20260717210000_AddMassTransitTransactionalOutbox";
    private const string RelationshipMigrationId =
        "20260717220000_AuthoritativeRelationshipStreams";

    [Fact]
    public void Relational_model_contains_the_complete_transactional_outbox_schema()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var inbox = AssertEntity(model, "MassTransit.EntityFrameworkCoreIntegration.InboxState");
        Assert.Equal("InboxState", inbox.GetTableName());
        AssertKey(inbox, false, "MessageId", "ConsumerId");
        AssertIndex(inbox, "IX_InboxState_Delivered", false, "Delivered");
        AssertConcurrencyToken(inbox, "RowVersion");

        var outboxMessage = AssertEntity(
            model,
            "MassTransit.EntityFrameworkCoreIntegration.OutboxMessage");
        Assert.Equal("OutboxMessage", outboxMessage.GetTableName());
        AssertKey(outboxMessage, true, "SequenceNumber");
        AssertIndex(
            outboxMessage,
            "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
            true,
            "InboxMessageId",
            "InboxConsumerId",
            "SequenceNumber");
        AssertIndex(
            outboxMessage,
            "IX_OutboxMessage_OutboxId_SequenceNumber",
            true,
            "OutboxId",
            "SequenceNumber");
        Assert.Equal(2, outboxMessage.GetForeignKeys().Count());
        Assert.Equal(256, outboxMessage.FindProperty("ContentType")!.GetMaxLength());
        Assert.Equal(256, outboxMessage.FindProperty("DestinationAddress")!.GetMaxLength());

        var outboxState = AssertEntity(
            model,
            "MassTransit.EntityFrameworkCoreIntegration.OutboxState");
        Assert.Equal("OutboxState", outboxState.GetTableName());
        AssertKey(outboxState, true, "OutboxId");
        AssertIndex(outboxState, "IX_OutboxState_Created", false, "Created");
        AssertConcurrencyToken(outboxState, "RowVersion");
    }

    [Fact]
    public void Transactional_outbox_migration_precedes_relationship_streams_and_is_expand_only()
    {
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        Assert.True(Array.IndexOf(migrations, MigrationId) >= 0);
        Assert.True(
            Array.IndexOf(migrations, MigrationId)
            < Array.IndexOf(migrations, RelationshipMigrationId));

        var operations = BuildOperations("Up");
        Assert.All(
            operations,
            operation => Assert.True(
                operation is CreateTableOperation or CreateIndexOperation,
                $"Unexpected non-expand operation: {operation.GetType().Name}"));

        Assert.Equal(
            ["InboxState", "OutboxMessage", "OutboxState"],
            operations.OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        var outboxMessage = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "OutboxMessage");
        Assert.Equal(2, outboxMessage.ForeignKeys.Count);
        Assert.Equal(6, operations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void Transactional_outbox_rollback_removes_only_its_three_tables()
    {
        var operations = BuildOperations("Down");

        Assert.All(operations, operation => Assert.IsType<DropTableOperation>(operation));
        Assert.Equal(
            ["InboxState", "OutboxMessage", "OutboxState"],
            operations.OfType<DropTableOperation>()
                .Select(operation => operation.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static FollowingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                "Server=localhost;Database=following_outbox_model_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new FollowingContext(options);
    }

    private static List<MigrationOperation> BuildOperations(string methodName)
    {
        var migration = new AddMassTransitTransactionalOutbox();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(AddMassTransitTransactionalOutbox).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        return builder.Operations.ToList();
    }

    private static IEntityType AssertEntity(IModel model, string name) =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(name));

    private static void AssertKey(
        IEntityType entity,
        bool primary,
        params string[] propertyNames)
    {
        var key = Assert.Single(
            entity.GetKeys(),
            candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(propertyNames, StringComparer.Ordinal));
        Assert.Equal(primary, key.IsPrimaryKey());
    }

    private static void AssertIndex(
        IEntityType entity,
        string databaseName,
        bool unique,
        params string[] propertyNames)
    {
        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == databaseName);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(propertyNames, index.Properties.Select(property => property.Name));
    }

    private static void AssertConcurrencyToken(IEntityType entity, string propertyName)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(propertyName));
        Assert.True(property.IsConcurrencyToken);
    }
}
