using Interaction.Domain.AggregatesModel;
using Interaction.Infraestructure.Database.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Interaction.UnitTests;

public sealed class InteractionSchemaTests
{
    [Fact]
    public void Schema_enforces_uniqueness_deduplication_bounds_and_outbox()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertKey(model.FindEntityType(typeof(Reaction)), "ResourceType", "ResourceId", "ActorId");
        AssertKey(model.FindEntityType(typeof(Rating)), "ResourceType", "ResourceId", "ActorId");
        var observation = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ViewObservation)));
        var dedupe = Assert.Single(observation.GetIndexes(), index => index.GetDatabaseName() == "UX_ViewObservation_Dedupe");
        Assert.True(dedupe.IsUnique);
        var summary = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(EngagementSummary)));
        Assert.Contains(summary.GetCheckConstraints(), check => check.Name == "CK_EngagementSummary_Nonnegative");
        Assert.Contains(summary.GetCheckConstraints(), check => check.Name == "CK_EngagementSummary_RatingTotal");
        Assert.NotNull(summary.FindProperty(nameof(EngagementSummary.HasExternalBaseline)));
        var erasure = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(InteractionUserErasure)));
        AssertKey(erasure, "UserId");
        Assert.Contains(erasure.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_InteractionUserErasure_Work"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    "Stage",
                    "RawViewsExpireAtUtc",
                    "LastProcessedAtUtc",
                    "LeaseUntilUtc",
                    "RequestedAtUtc",
                    "UserId",
                }));
        Assert.NotNull(model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.InboxState"));
        Assert.NotNull(model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.OutboxMessage"));
        Assert.NotNull(model.FindEntityType("MassTransit.EntityFrameworkCoreIntegration.OutboxState"));
    }

    [Fact]
    public void Initial_migration_is_additive_and_contains_service_and_transport_tables()
    {
        using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var initialId = Assert.Single(assembly.Migrations, item =>
            item.Key.EndsWith("_InitialInteractionSchema", StringComparison.Ordinal)).Value;
        var migration = assembly.CreateMigration(initialId, "Pomelo.EntityFrameworkCore.MySql");
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(Migration).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        Assert.All(builder.Operations, operation => Assert.True(
            operation is AlterDatabaseOperation or CreateTableOperation or CreateIndexOperation,
            $"Initial migration must be additive, but found {operation.GetType().Name}."));
        var tables = builder.Operations.OfType<CreateTableOperation>().Select(item => item.Name).ToHashSet();
        foreach (var table in new[] { "ResourceStates", "ResourceStateGaps", "Reactions", "Ratings", "ViewObservations", "EngagementSummaries", "InboxState", "OutboxMessage", "OutboxState" })
            Assert.Contains(table, tables);
    }

    [Fact]
    public void Retirement_migration_removes_comment_import_state_and_refuses_reconstruction()
    {
        using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var migrationId = Assert.Single(assembly.Migrations, item =>
            item.Key.EndsWith("_RemoveRetiredCommentImportState", StringComparison.Ordinal)).Value;
        var migration = assembly.CreateMigration(migrationId, "Pomelo.EntityFrameworkCore.MySql");
        var up = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(Migration).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [up]);
        Assert.Contains(up.Operations.OfType<DropTableOperation>(), operation =>
            operation.Name == "InteractionIntentCursors");

        var down = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var exception = Record.Exception(() =>
            typeof(Migration).GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(migration, [down]));
        Assert.NotNull(exception);
        var refusal = Assert.IsType<NotSupportedException>(exception.GetBaseException());
        Assert.Contains("cannot be restored", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(down.Operations);
    }

    [Fact]
    public void User_erasure_migration_adds_permanent_tombstone_and_preserves_rollback()
    {
        using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var migrationId = Assert.Single(assembly.Migrations, item =>
            item.Key.EndsWith(
                "_AddDurableInteractionUserErasure",
                StringComparison.Ordinal)).Value;
        var migration = assembly.CreateMigration(
            migrationId,
            "Pomelo.EntityFrameworkCore.MySql");
        var up = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(Migration).GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [up]);
        Assert.Contains(up.Operations.OfType<CreateTableOperation>(), operation =>
            operation.Name == "InteractionUserErasures");
        Assert.Contains(up.Operations.OfType<CreateIndexOperation>(), operation =>
            operation.Name == "IX_InteractionUserErasure_Work");

        var down = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(Migration).GetMethod(
                "Down",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [down]);
        Assert.Contains(down.Operations.OfType<DropTableOperation>(), operation =>
            operation.Name == "InteractionUserErasures");
    }

    [Fact]
    public void Model_snapshot_has_no_drift()
    {
        using var context = CreateContext();
        var services = ((IInfrastructure<IServiceProvider>)context).Instance;
        var snapshot = services.GetRequiredService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);
        var initializer = services.GetRequiredService<IModelRuntimeInitializer>();
        var source = initializer.Initialize(snapshot!.Model, designTime: true);
        var target = services.GetRequiredService<IDesignTimeModel>().Model;
        var differences = services.GetRequiredService<IMigrationsModelDiffer>().GetDifferences(
            source.GetRelationalModel(), target.GetRelationalModel());
        Assert.Empty(differences);
    }

    [Fact]
    public void Public_contracts_never_expose_moderation_or_report_data()
    {
        var contracts = typeof(Interaction.Contracts.EngagementSummaryChanged)
            .Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "Interaction.Contracts")
            .ToArray();

        Assert.DoesNotContain(contracts, type =>
            type.Name.Contains("Report", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Moderation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            contracts.SelectMany(type => type.GetProperties()),
            property =>
                property.Name.Contains("Report", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Moderation", StringComparison.OrdinalIgnoreCase));
    }

    private static InteractionDbContext CreateContext() => new(
        new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql("Server=localhost;Database=interaction_model;User=model;Password=model;", new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options);

    private static void AssertKey(IEntityType? entity, params string[] properties)
    {
        var key = Assert.IsAssignableFrom<IKey>(Assert.IsAssignableFrom<IEntityType>(entity).FindPrimaryKey());
        Assert.Equal(properties, key.Properties.Select(property => property.Name));
    }
}
