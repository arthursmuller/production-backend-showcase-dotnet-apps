using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Moderation.Domain.AggregatesModel;
using Moderation.Infra.Database.Ef;
using Moderation.Infra.Database.Ef.Migrations;

namespace Moderation.UnitTests;

public sealed class ModerationPersistenceTests
{
    [Fact]
    public void Service_owned_model_has_constraints_indexes_and_optimistic_concurrency()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var reports = Entity<ModerationReport>(model);
        Assert.Equal("ModerationReports", reports.GetTableName());
        AssertUniqueIndex(reports, "UX_ModerationReports_Reporter_Idempotency");
        Assert.Contains(reports.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationReports_RequestHash");

        var cases = Entity<ModerationCase>(model);
        Assert.True(cases.FindProperty(nameof(ModerationCase.Version))!.IsConcurrencyToken);
        AssertUniqueIndex(cases, "IX_ModerationCases_ReportId");
        Assert.Contains(cases.GetIndexes(), index => index.GetDatabaseName() == "IX_ModerationCases_SlaQueue");
        Assert.Contains(cases.GetCheckConstraints(), constraint => constraint.Name == "CK_ModerationCases_Status");
        Assert.Contains(cases.GetIndexes(), index => index.GetDatabaseName() == "IX_ModerationCases_Retention");
        Assert.Contains(cases.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationCases_RetentionClaim");
        Assert.Contains(cases.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_ModerationCases_ErasureClaim");
        Assert.Contains(cases.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationCases_ErasureClaim");
        Assert.Contains(cases.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_ModerationCases_ErasureSubjectHash");
        Assert.Contains(cases.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationCases_ErasureSubjectHash");

        var erasures = Entity<ModerationUserErasure>(model);
        Assert.Equal("ModerationUserErasures", erasures.GetTableName());
        AssertUniqueIndex(erasures, "UX_ModerationUserErasures_Request");
        Assert.Contains(erasures.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_ModerationUserErasures_Work");

        var retention = Entity<ModerationRetentionCheckpoint>(model);
        Assert.Equal("ModerationRetentionCheckpoints", retention.GetTableName());
        Assert.Contains(retention.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationRetentionCheckpoints_Singleton");

        var audits = Entity<ModerationAuditEntry>(model);
        AssertUniqueIndex(audits, "IX_ModerationAuditEntries_CaseId_CaseVersion");
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(audits.GetForeignKeys()).DeleteBehavior);

        var holds = Entity<LegalHold>(model);
        AssertUniqueIndex(holds, "UX_ModerationLegalHolds_ActiveCase");
        Assert.Contains(holds.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationLegalHolds_Release");

        var messages = Entity<ModerationCaseMessage>(model);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(messages.GetForeignKeys()).DeleteBehavior);
        Assert.Contains(messages.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ModerationCaseMessages_Hash");
    }

    [Fact]
    public void Relational_model_contains_complete_MassTransit_inbox_and_outbox_schema()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var inbox = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.InboxState"));
        var outboxMessage = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.OutboxMessage"));
        var outboxState = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.OutboxState"));

        Assert.Equal("InboxState", inbox.GetTableName());
        Assert.Equal("OutboxMessage", outboxMessage.GetTableName());
        Assert.Equal("OutboxState", outboxState.GetTableName());
        Assert.True(inbox.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Equal(2, outboxMessage.GetForeignKeys().Count());
        Assert.True(outboxState.FindProperty("RowVersion")!.IsConcurrencyToken);
    }

    [Fact]
    public void DbContext_rejects_audit_updates_before_provider_io()
    {
        using var context = CreateContext();
        var pair = ModerationTestFactory.CreateCase();
        context.Add(pair.Report);
        context.Add(pair.Case);
        var audit = Assert.Single(pair.Case.AuditEntries);
        context.Entry(audit).State = EntityState.Modified;

        var exception = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_adds_database_level_append_only_audit_triggers()
    {
        var operations = BuildOperations("Up");
        var sql = operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();

        Assert.Contains(sql, statement => statement.Contains("TR_ModerationAuditEntries_RejectUpdate", StringComparison.Ordinal));
        Assert.Contains(sql, statement => statement.Contains("TR_ModerationAuditEntries_RejectDelete", StringComparison.Ordinal));
        Assert.Contains(sql, statement => statement.Contains("TR_ModerationCaseMessages_RejectUpdate", StringComparison.Ordinal));
        Assert.Contains(sql, statement => statement.Contains("TR_ModerationCaseMessages_RejectDelete", StringComparison.Ordinal));
        Assert.Contains(operations.OfType<CreateTableOperation>(), table => table.Name == "ModerationReports");
        Assert.Contains(operations.OfType<CreateTableOperation>(), table => table.Name == "OutboxMessage");
    }

    [Fact]
    public void Initial_migration_rollback_drops_triggers_before_owned_tables()
    {
        var operations = BuildOperations("Down");
        var firstTableDrop = operations.FindIndex(operation => operation is DropTableOperation);

        Assert.True(firstTableDrop >= 2);
        Assert.All(operations.Take(firstTableDrop), operation => Assert.IsType<SqlOperation>(operation));
        Assert.Contains(operations.OfType<DropTableOperation>(), table => table.Name == "ModerationReports");
        Assert.Contains(operations.OfType<DropTableOperation>(), table => table.Name == "OutboxState");
    }

    [Fact]
    public void Retention_migration_keeps_append_only_deletes_fenced_by_a_live_database_lease()
    {
        var migration = new AddLegalHoldSafeModerationRetention();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(AddLegalHoldSafeModerationRetention).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        var sql = builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();

        Assert.Contains(sql, statement =>
            statement.Contains("@moderation_retention_fencing_token", StringComparison.Ordinal)
            && statement.Contains("ModerationRetentionCheckpoints", StringComparison.Ordinal)
            && statement.Contains("ModerationAuditEntries", StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains("@moderation_retention_fencing_token", StringComparison.Ordinal)
            && statement.Contains("ModerationRetentionCheckpoints", StringComparison.Ordinal)
            && statement.Contains("ModerationCaseMessages", StringComparison.Ordinal));
    }

    [Fact]
    public void Erasure_migration_adds_tombstones_and_requires_a_live_subject_fence()
    {
        var migration = new AddDurableModerationUserErasure();
        var up = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(AddDurableModerationUserErasure).GetMethod(
                "Up",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [up]);

        Assert.Contains(up.Operations.OfType<CreateTableOperation>(), table =>
            table.Name == "ModerationUserErasures");
        var sql = up.Operations.OfType<SqlOperation>()
            .Select(operation => operation.Sql)
            .ToArray();
        Assert.Contains(sql, statement =>
            statement.Contains("@moderation_erasure_user_id", StringComparison.Ordinal)
            && statement.Contains("@moderation_erasure_fencing_token", StringComparison.Ordinal)
            && statement.Contains("ModerationUserErasures", StringComparison.Ordinal)
            && statement.Contains("ModerationAuditEntries", StringComparison.Ordinal));

        var down = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(AddDurableModerationUserErasure).GetMethod(
                "Down",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [down]);
        Assert.Contains(down.Operations.OfType<DropTableOperation>(), table =>
            table.Name == "ModerationUserErasures");
        Assert.Contains(down.Operations.OfType<SqlOperation>(), operation =>
            operation.Sql.Contains(
                "@moderation_retention_fencing_token",
                StringComparison.Ordinal)
            && !operation.Sql.Contains(
                "@moderation_erasure_user_id",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Model_snapshot_matches_runtime_model()
    {
        using var context = CreateContext();
        var snapshotType = typeof(ModerationDbContext).Assembly.GetType(
            "Moderation.Infra.Database.Ef.Migrations.ModerationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(
            Activator.CreateInstance(snapshotType, nonPublic: true));
        var current = context.GetService<IDesignTimeModel>().Model;
        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var differ = context.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            current.GetRelationalModel());

        Assert.True(
            operations.Count == 0,
            "Model snapshot drift: " + string.Join(", ", operations.Select(operation => operation.GetType().Name)));
    }

    [Fact]
    public void Retirement_migration_removes_source_request_state_and_refuses_reconstruction()
    {
        var migration = new RemoveRetiredModerationSourceRequests();
        var up = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(RemoveRetiredModerationSourceRequests).GetMethod(
                "Up",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [up]);
        Assert.Equal(2, up.Operations.OfType<DropColumnOperation>().Count());
        Assert.All(up.Operations.OfType<DropColumnOperation>(), operation =>
            Assert.Equal("SourceRequestId", operation.Name));

        var down = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var exception = Record.Exception(() =>
            typeof(RemoveRetiredModerationSourceRequests).GetMethod(
                    "Down",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(migration, [down]));
        Assert.NotNull(exception);
        var refusal = Assert.IsType<NotSupportedException>(exception.GetBaseException());
        Assert.Contains("cannot be restored", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(down.Operations);
    }

    private static ModerationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(
                "Server=127.0.0.1;Database=moderation_model_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new ModerationDbContext(options);
    }

    private static List<MigrationOperation> BuildOperations(string methodName)
    {
        var migration = new InitialModerationSchema();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var method = typeof(InitialModerationSchema).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        return builder.Operations.ToList();
    }

    private static IEntityType Entity<TEntity>(IModel model) where TEntity : class =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(TEntity)));

    private static void AssertUniqueIndex(IEntityType entity, string databaseName)
    {
        var index = Assert.Single(entity.GetIndexes(), candidate => candidate.GetDatabaseName() == databaseName);
        Assert.True(index.IsUnique);
    }
}
