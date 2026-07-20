using Following.Domain.AggregatesModel;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingPersistenceModelTests
{
    private const string FirstMigration = "20260527011917_first";
    private const string HardeningMigration = "20260717190000_HardenFollowingRelationships";

    [Fact]
    public void Persistence_model_uses_scalar_relationship_keys_and_database_invariants()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var relationshipEntities = model.GetEntityTypes()
            .Where(entity => typeof(FollowingEntity).IsAssignableFrom(entity.ClrType))
            .ToArray();
        Assert.DoesNotContain(relationshipEntities, entity => entity.IsOwned());
        Assert.Empty(relationshipEntities.SelectMany(entity => entity.GetForeignKeys()));

        var follow = AssertEntity<Follow>(model);
        Assert.Equal("User_Id", follow.FindProperty(nameof(Follow.FollowerUserId))!.GetColumnName());
        Assert.Equal("Followed_Id", follow.FindProperty(nameof(Follow.FollowedUserId))!.GetColumnName());
        AssertUniqueIndex(follow, "UX_Follow_Direction");

        var request = AssertEntity<FriendRequest>(model);
        AssertUniqueIndex(request, "UX_FriendRequest_ActivePair");
        Assert.NotNull(request.FindProperty("PairMinUserId")!.GetComputedColumnSql());
        Assert.NotNull(request.FindProperty("ActivePairMinUserId")!.GetComputedColumnSql());

        var friendship = AssertEntity<Friendship>(model);
        AssertUniqueIndex(friendship, "UX_Friendship_Direction");
        Assert.NotNull(friendship.FindProperty("PairMinUserId")!.GetComputedColumnSql());

        var categoryFollow = AssertEntity<CategoryFollow>(model);
        AssertUniqueIndex(categoryFollow, "UX_CategoryFollow_User_Category");

        var subscription = AssertEntity<FriendshipSubscriptionFeature>(model);
        AssertUniqueIndex(
            subscription,
            "UX_SubscriptionFeature_Owner_Requester_Subscription");
        Assert.Equal(
            FollowingFieldLimits.SubscriptionId,
            subscription.FindProperty(nameof(FriendshipSubscriptionFeature.SubscriptionId))!.GetMaxLength());

        foreach (var entity in model.GetEntityTypes()
                     .Where(entity => typeof(FollowingEntity).IsAssignableFrom(entity.ClrType)))
        {
            var version = entity.FindProperty(nameof(FollowingEntity.Version));
            Assert.NotNull(version);
            Assert.True(version!.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        }

        var analytics = AssertEntity<UserRelationAnalytics>(model);
        Assert.Equal(ValueGenerated.Never, analytics.FindProperty(nameof(UserRelationAnalytics.Id))!.ValueGenerated);
    }

    [Fact]
    public void Hardening_migration_repairs_rows_before_unique_indexes_and_keeps_audit_data()
    {
        using var context = CreateContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            FirstMigration,
            HardeningMigration);

        var repairLog = script.IndexOf(
            "CREATE TABLE IF NOT EXISTS `FollowingRelationshipRepairLog`",
            StringComparison.Ordinal);
        var duplicateRepair = script.IndexOf(
            "'DeletedDuplicateActivePair'",
            StringComparison.Ordinal);
        var uniqueConstraint = script.IndexOf(
            "CREATE UNIQUE INDEX `UX_FriendRequest_ActivePair`",
            StringComparison.Ordinal);

        Assert.True(repairLog >= 0);
        Assert.True(duplicateRepair > repairLog);
        Assert.True(uniqueConstraint > duplicateRepair);
        Assert.Contains("InsertedMissingReverse", script, StringComparison.Ordinal);
        Assert.Contains("ReconciledCounters", script, StringComparison.Ordinal);
        Assert.Contains("TR_UserRelationAnalytics_Version", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_snapshot_matches_the_runtime_model()
    {
        using var context = CreateContext();
        var snapshotType = typeof(FollowingContext).Assembly.GetType(
            "Following.Infra.Migrations.FollowingContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(
            Activator.CreateInstance(snapshotType, nonPublic: true));
        var currentModel = context.GetService<IDesignTimeModel>().Model;
        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var differ = context.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.True(
            operations.Count == 0,
            "Model snapshot drift: " + string.Join(", ", operations.Select(Describe)));
    }

    private static FollowingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                "Server=localhost;Database=following_model_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        return new FollowingContext(options);
    }

    private static IEntityType AssertEntity<TEntity>(IModel model) where TEntity : class =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(TEntity)));

    private static void AssertUniqueIndex(IEntityType entity, string databaseName)
    {
        var index = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == databaseName);
        Assert.True(index.IsUnique);
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        ColumnOperation column => $"{operation.GetType().Name}({column.Table}.{column.Name})",
        CreateIndexOperation index => $"CreateIndex({index.Table}.{index.Name})",
        DropIndexOperation index => $"DropIndex({index.Table}.{index.Name})",
        AddCheckConstraintOperation check => $"AddCheck({check.Table}.{check.Name})",
        DropCheckConstraintOperation check => $"DropCheck({check.Table}.{check.Name})",
        _ => operation.GetType().Name
    };
}
