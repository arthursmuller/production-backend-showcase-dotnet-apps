using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedSchemaModelTests
{
    [Fact]
    public void Feed_model_enforces_idempotency_and_concurrency()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertUniqueIndex(
            model.FindEntityType(typeof(FeedActivity)),
            "UX_FeedActivity_Target_Kind");
        AssertUniqueIndex(
            model.FindEntityType(typeof(FeedEntry)),
            "UX_FeedEntry_Recipient_Activity");
        AssertUniqueIndex(
            model.FindEntityType(typeof(FeedAudienceEdge)),
            "UX_FeedAudienceEdge_Direction_Kind");
        AssertUniqueIndex(
            model.FindEntityType(typeof(FanoutWorkItem)),
            "UX_FanoutWorkItem_SourceEventId");

        foreach (var entityType in new[]
                 {
                     typeof(FeedActivity),
                     typeof(FeedEntry),
                     typeof(FeedAudienceEdge),
                     typeof(FeedUserState),
                     typeof(FanoutWorkItem),
                 })
        {
            var version = model.FindEntityType(entityType)?.FindProperty("Version");
            Assert.NotNull(version);
            Assert.True(version!.IsConcurrencyToken);
        }
    }

    [Fact]
    public void Feed_model_has_explicit_safe_delete_behaviour_and_cursor_indexes()
    {
        using var context = CreateContext();
        var model = context.Model;

        var entryType = model.FindEntityType(typeof(FeedEntry));
        Assert.NotNull(entryType);
        var entryForeignKey = Assert.Single(entryType!.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, entryForeignKey.DeleteBehavior);

        var workType = model.FindEntityType(typeof(FanoutWorkItem));
        Assert.NotNull(workType);
        var workForeignKey = Assert.Single(workType!.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, workForeignKey.DeleteBehavior);

        Assert.NotNull(
            model.FindEntityType(typeof(FeedEntry))?.GetIndexes()
                .SingleOrDefault(index =>
                    index.GetDatabaseName() == "IX_FeedEntry_Recipient_State_Cursor"));
    }

    [Fact]
    public void Model_snapshot_matches_the_runtime_model()
    {
        using var context = CreateContext();
        var services = ((IInfrastructure<IServiceProvider>)context).Instance;
        var migrationsAssembly = services.GetRequiredService<IMigrationsAssembly>();
        var snapshot = migrationsAssembly.ModelSnapshot;
        Assert.NotNull(snapshot);
        var initializer = services.GetRequiredService<IModelRuntimeInitializer>();
        var source = initializer.Initialize(snapshot!.Model, designTime: true);
        var target = services.GetRequiredService<IDesignTimeModel>().Model;
        var differ = services.GetRequiredService<IMigrationsModelDiffer>();
        var differences = differ.GetDifferences(
            source.GetRelationalModel(),
            target.GetRelationalModel());

        Assert.True(
            differences.Count == 0,
            "Pending model operations: " + string.Join(", ", differences.Select(Describe)));
    }

    private static FeedContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                "Server=localhost;Port=3306;Database=feed_model_tests;User=root;Password=not-used;",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        return new FeedContext(options);
    }

    private static void AssertUniqueIndex(IEntityType? entityType, string databaseName)
    {
        Assert.NotNull(entityType);
        var index = entityType!.GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == databaseName);
        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        CreateTableOperation create => $"CreateTable:{create.Name}",
        DropTableOperation drop => $"DropTable:{drop.Name}",
        AddColumnOperation add => $"AddColumn:{add.Table}.{add.Name}",
        AlterColumnOperation alter =>
            $"AlterColumn:{alter.Table}.{alter.Name}" +
            $" new(type={alter.ColumnType},nullable={alter.IsNullable},clr={alter.ClrType.Name},default={alter.DefaultValue})" +
            $" old(type={alter.OldColumn.ColumnType},nullable={alter.OldColumn.IsNullable},clr={alter.OldColumn.ClrType.Name},default={alter.OldColumn.DefaultValue})",
        CreateIndexOperation index => $"CreateIndex:{index.Table}.{index.Name}",
        DropIndexOperation index => $"DropIndex:{index.Table}.{index.Name}",
        AddForeignKeyOperation foreignKey => $"AddForeignKey:{foreignKey.Table}.{foreignKey.Name}",
        _ => operation.GetType().Name,
    };
}
