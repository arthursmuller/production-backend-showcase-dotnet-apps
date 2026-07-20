using Comment.Infra.Database.Ef.Migrations;
using Comment.Infra.Persistence;
using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentPersistenceModelTests
{
    [Fact]
    public void Runtime_model_contains_only_canonical_comment_and_authoritative_projection_tables()
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CommentPost", tables);
        Assert.Contains("CommentInteractionSummaryProjections", tables);
        Assert.Contains("CommentInteractionActorProjections", tables);
        Assert.Contains("CommentMediaIngestion", tables);
        Assert.Contains("CommentAudienceProjectionState", tables);
        Assert.Contains("CommentUserErasure", tables);
        Assert.Contains("CommentModerationDecisionProjection", tables);
        Assert.Contains("InboxState", tables);
        Assert.Contains("OutboxMessage", tables);
        Assert.Contains("OutboxState", tables);
        Assert.DoesNotContain("CommentSocialAnalytics", tables);

        var comment = context.Model.FindEntityType(typeof(CommentPost))!;
        Assert.Null(comment.FindProperty("IdOfRelatedEntity"));
        Assert.Null(comment.FindProperty("TypeOfRelatedEntity"));
        Assert.Null(comment.FindProperty("RelatedEntity"));
        Assert.Null(comment.FindProperty("SocialId"));
        Assert.NotNull(comment.FindProperty(nameof(CommentPost.TargetId)));
        Assert.NotNull(comment.FindProperty(nameof(CommentPost.TargetType)));
        Assert.NotNull(comment.FindProperty(nameof(CommentPost.VisibilityLevelId)));
        Assert.DoesNotContain(context.Model.GetEntityTypes(), entity =>
            string.Equals(
                entity.ClrType.Name,
                "VisibilityLevelRecord",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Retirement_migration_drops_retired_tables_and_remaps_erasure_progress()
    {
        var migration = new RemoveRetiredCommentArtifacts();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(RemoveRetiredCommentArtifacts)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        Assert.Equal(14, builder.Operations.OfType<DropTableOperation>().Count());

        var sql = string.Join('\n', builder.Operations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));
        Assert.Contains("`Stage` BETWEEN 3 AND 9", sql, StringComparison.Ordinal);
        Assert.Contains("`Stage` BETWEEN 10 AND 17", sql, StringComparison.Ordinal);
        Assert.Contains("`Stage` - 7", sql, StringComparison.Ordinal);

        var constraint = Assert.Single(
            builder.Operations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name == "CK_CmtUserErasure_Stage");
        Assert.Equal("`Stage` BETWEEN 1 AND 10", constraint.Sql);
    }

    [Fact]
    public void Compatibility_cleanup_migration_drops_only_retired_comment_storage()
    {
        var migration = new RemoveCommentCompatibilityArtifacts();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(RemoveCommentCompatibilityArtifacts)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var orderedDroppedColumns = builder.Operations
            .OfType<DropColumnOperation>()
            .Select(operation => (operation.Table, operation.Name))
            .ToArray();
        var droppedColumns = orderedDroppedColumns
            .ToHashSet();
        Assert.Equal(6, droppedColumns.Count);
        Assert.Contains(("CommentPost", "RelatedEntity"), droppedColumns);
        Assert.Contains(("CommentPost", "SocialId"), droppedColumns);
        Assert.Contains(("CommentPost", "IdOfRelatedEntity"), droppedColumns);
        Assert.Contains(("CommentPost", "TypeOfRelatedEntity"), droppedColumns);
        Assert.Contains(("CommentPost", "VisibilityLevel_Id"), droppedColumns);
        Assert.Contains(("CommentPost", "VisibilityLevel_Name"), droppedColumns);
        Assert.True(
            Array.IndexOf(orderedDroppedColumns, ("CommentPost", "RelatedEntity")) <
            Array.IndexOf(orderedDroppedColumns, ("CommentPost", "IdOfRelatedEntity")));
        Assert.True(
            Array.IndexOf(orderedDroppedColumns, ("CommentPost", "RelatedEntity")) <
            Array.IndexOf(orderedDroppedColumns, ("CommentPost", "TypeOfRelatedEntity")));

        var droppedTable = Assert.Single(builder.Operations.OfType<DropTableOperation>());
        Assert.Equal("CommentSocialAnalytics", droppedTable.Name);
        Assert.Contains(builder.Operations.OfType<DropForeignKeyOperation>(), operation =>
            operation.Name == "FK_CmtPost_Analytics" && operation.Table == "CommentPost");
        Assert.Contains(builder.Operations.OfType<DropIndexOperation>(), operation =>
            operation.Name == "UX_CommentPost_SocialId" && operation.Table == "CommentPost");

        var visibilityConstraint = Assert.Single(
            builder.Operations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name == "CK_CmtPost_VisibilityLevel");
        Assert.Equal("`VisibilityLevelId` IN (1, 2, 3, 4)", visibilityConstraint.Sql);
    }

    [Fact]
    public void Compatibility_cleanup_down_refuses_before_emitting_ddl()
    {
        var migration = new RemoveCommentCompatibilityArtifacts();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");

        var exception = Assert.Throws<TargetInvocationException>(() =>
            typeof(RemoveCommentCompatibilityArtifacts)
                .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(migration, [builder]));

        var refusal = Assert.IsType<NotSupportedException>(exception.InnerException);
        Assert.Equal(
            "Retired Comment compatibility columns and engagement data cannot be restored.",
            refusal.Message);
        Assert.Empty(builder.Operations);
    }

    private static CommentContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(
                "Server=localhost;Database=comment_model;User=model;Password=model",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        return new CommentContext(options, mediator: null!);
    }
}
