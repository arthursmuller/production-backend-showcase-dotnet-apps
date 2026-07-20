using Comment.Application.Dtos;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Comment.Domain.Dtos;
using Comment.Domain.Repositories;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Repositories;
using Comment.Infra.Persistence.Repositories.Interactions;
using Interaction.Contracts;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using StarterEntities;
using System.Reflection;
using Xunit;

namespace Comment.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommentMariaDbCollection
{
    public const string Name = "Comment isolated MariaDB";
}

[Collection(CommentMariaDbCollection.Name)]
public sealed class CommentCursorMariaDbIntegrationTests
{
    private const string ConnectionEnvironment = "COMMENT_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Keyset_pages_are_stable_without_count_or_offset_under_concurrent_insert()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = Options(connectionString);
        await using var context = new CommentContext(options, mediator: null!);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var targetId = $"cursor-{Guid.NewGuid():N}";
        var ownerId = Random.Shared.Next(100_000, 1_000_000);
        var oldest = CreateComment(ownerId, targetId, new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc));
        var middle = CreateComment(ownerId, targetId, new DateTime(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc));
        var newest = CreateComment(ownerId, targetId, new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc));
        context.Comments.AddRange(oldest, middle, newest);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new CommentReadRepository(context);
        var criteria = new CommentQueryCriteria
        {
            Targets = [new CommentTarget(targetId, "post")],
        };
        var first = await repository.ListCursorAsync(
            criteria,
            after: null!,
            pageSize: 2,
            TestContext.Current.CancellationToken);

        Assert.True(first.HasMore);
        Assert.Equal([newest.Id, middle.Id], first.Items.Select(item => item.Id));

        var concurrent = CreateComment(
            ownerId,
            targetId,
            new DateTime(2026, 7, 17, 13, 0, 0, DateTimeKind.Utc));
        context.Comments.Add(concurrent);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var pagePosition = new CommentCursorPosition(
            first.Items[^1].CreatedDateUtc,
            first.Items[^1].Id);
        var second = await repository.ListCursorAsync(
            criteria,
            pagePosition,
            pageSize: 2,
            TestContext.Current.CancellationToken);

        Assert.False(second.HasMore);
        Assert.Equal([oldest.Id], second.Items.Select(item => item.Id));
        Assert.DoesNotContain(concurrent.Id, second.Items.Select(item => item.Id));
        Assert.Empty(first.Items.Select(item => item.Id)
            .Intersect(second.Items.Select(item => item.Id)));

        await AssertCursorPlanAsync(
            connectionString,
            targetId,
            pagePosition,
            TestContext.Current.CancellationToken);
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Interaction_status_and_summary_are_read_from_authoritative_projections()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        await using var context = new CommentContext(Options(connectionString), mediator: null!);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var commentId = Random.Shared.Next(1, int.MaxValue);
        var actorId = Random.Shared.Next(1, int.MaxValue);
        var observedAt = DateTimeOffset.UtcNow;
        var actor = new CommentInteractionActorProjection(commentId, actorId);
        actor.ApplyReaction(CommentReactionKind.Dislike, 4, observedAt);
        actor.ApplyRating(6, 3, observedAt);
        context.CommentInteractionActorProjections.Add(actor);
        context.CommentInteractionSummaryProjections.Add(
            new CommentInteractionSummaryProjection(
                commentId,
                new CommentInteractionSummarySnapshot(
                    7,
                    2,
                    3,
                    12,
                    99,
                    4,
                    5,
                    8,
                    observedAt)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var status = Assert.Single(await new CommentReadRepository(context)
            .GetInteractionStatusesAsync(
                [commentId],
                actorId,
                TestContext.Current.CancellationToken));
        Assert.False(status.Liked);
        Assert.True(status.Disliked);
        Assert.True(status.Rated);

        var summaries = await new CommentInteractionProjectionStore(
                context,
                AllowAllCommentUserErasureAuthority.Instance)
            .GetSummariesAsync([commentId], TestContext.Current.CancellationToken);
        var summary = Assert.Single(summaries).Value;
        Assert.Equal(7, summary.LikeCount);
        Assert.Equal(99, summary.ViewCount);
        Assert.False(summary.HasGap);
    }

    private static DbContextOptions<CommentContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;

    private static CommentPost CreateComment(
        int ownerId,
        string targetId,
        DateTime createdDate)
    {
        var comment = new CommentPost(
            new User(ownerId, $"cursor-owner-{ownerId}"),
            new AddCommentDomainDto
            {
                Description = $"cursor-test-{createdDate:O}",
                TargetId = targetId,
                TargetType = "post",
                VisibilityLevelId = VisibilityLevel.Global.Id,
            },
            Array.Empty<NewCommentMedia>());
        typeof(BaseEntity)
            .GetField("_createdDate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(comment, createdDate);
        return comment;
    }

    private static async Task AssertCursorPlanAsync(
        string connectionString,
        string targetId,
        CommentCursorPosition after,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            """
            EXPLAIN SELECT `Id`
            FROM `CommentPost`
            WHERE `TargetType` = 'post'
              AND `TargetId` = @targetId
              AND `VisibilityLevelId` = 1
              AND (`CreatedDate` < @createdAt
                   OR (`CreatedDate` = @createdAt AND `Id` < @id))
            ORDER BY `CreatedDate` DESC, `Id` DESC
            LIMIT 51
            """,
            connection);
        command.Parameters.AddWithValue("@targetId", targetId);
        command.Parameters.AddWithValue("@createdAt", after.CreatedDateUtc);
        command.Parameters.AddWithValue("@id", after.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        var chosenKey = reader.IsDBNull(reader.GetOrdinal("key"))
            ? string.Empty
            : reader.GetString(reader.GetOrdinal("key"));
        Assert.Equal("IX_CommentPost_Target_Visibility_Created_Id", chosenKey);
    }
}
