using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StarterEntities;
using System.Reflection;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingCursorRepositoryTests
{
    [Fact]
    public void Cursor_queries_use_keyset_predicates_page_size_plus_one_and_no_offset_or_count()
    {
        using var context = CreateContext();
        var after = new FollowingCursorPosition(
            new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc),
            900);
        var window = new FollowingCursorWindow(100, after);

        var sqlStatements = new[]
        {
            FollowingCursorQueries.FriendRequests(
                context.FriendRequests.AsNoTracking(),
                42,
                sentRequests: false,
                FriendRequestStatus.Pending,
                window).ToQueryString(),
            FollowingCursorQueries.Friends(
                context.Friendships.AsNoTracking(), 42, window).ToQueryString(),
            FollowingCursorQueries.Follows(
                context.Follows.AsNoTracking(), 42, following: true, window).ToQueryString(),
            FollowingCursorQueries.CategoryFollows(
                context.CategoryFollows.AsNoTracking(), 42, window).ToQueryString(),
            FollowingCursorQueries.UserBlocks(
                context.UserBlocks.AsNoTracking(), 42, window).ToQueryString(),
            FollowingCursorQueries.FollowerRelationIds(
                context.Follows.AsNoTracking(), 42, window).ToQueryString(),
        };

        foreach (var sql in sqlStatements)
        {
            Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CreatedDate", sql, StringComparison.Ordinal);
            Assert.Contains("Id", sql, StringComparison.Ordinal);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Status", sqlStatements[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Stable_keyset_traversal_excludes_a_newer_concurrent_insert_without_duplicates()
    {
        var owner = new User(42, "owner");
        var source = new List<Follow>
        {
            CreateFollow(1, owner, new User(101, "oldest"), Utc(10)),
            CreateFollow(2, owner, new User(102, "middle"), Utc(11)),
            CreateFollow(3, owner, new User(103, "newest"), Utc(12)),
        };

        var firstRows = FollowingCursorQueries.Follows(
                source.AsQueryable(),
                owner.Id,
                following: true,
                new FollowingCursorWindow(2, null!))
            .ToArray();
        var first = FollowingCursorQueries.ToSlice(firstRows, 2);
        Assert.True(first.HasMore);
        Assert.Equal([3, 2], first.Items.Select(item => item.RowId));

        source.Add(CreateFollow(4, owner, new User(104, "concurrent"), Utc(13)));

        var secondRows = FollowingCursorQueries.Follows(
                source.AsQueryable(),
                owner.Id,
                following: true,
                new FollowingCursorWindow(2, first.NextPosition))
            .ToArray();
        var second = FollowingCursorQueries.ToSlice(secondRows, 2);

        Assert.False(second.HasMore);
        Assert.Equal([1], second.Items.Select(item => item.RowId));
        Assert.DoesNotContain(4, second.Items.Select(item => item.RowId));
        Assert.Empty(first.Items.Select(item => item.RowId)
            .Intersect(second.Items.Select(item => item.RowId)));
    }

    [Fact]
    public void Same_timestamp_rows_use_descending_id_as_the_deterministic_tie_breaker()
    {
        var owner = new User(42, "owner");
        var timestamp = Utc(12);
        var source = new[]
        {
            CreateFollow(7, owner, new User(101, "first"), timestamp),
            CreateFollow(9, owner, new User(102, "second"), timestamp),
            CreateFollow(8, owner, new User(103, "third"), timestamp),
        };

        var rows = FollowingCursorQueries.Follows(
                source.AsQueryable(),
                owner.Id,
                following: true,
                new FollowingCursorWindow(3, null!))
            .ToArray();

        Assert.Equal([9, 8, 7], rows.Select(item => item.RowId));
    }

    [Fact]
    public void Runtime_model_has_every_composite_index_required_by_cursor_queries()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertIndex<FriendRequest>(model, "IX_FriendRequest_Sender_Status_Cursor",
            [nameof(FriendRequest.SenderUserId), nameof(FriendRequest.Status), nameof(FriendRequest.CreatedDate), nameof(FriendRequest.Id)],
            [false, false, true, true]);
        AssertIndex<FriendRequest>(model, "IX_FriendRequest_Recipient_Status_Cursor",
            [nameof(FriendRequest.RecipientUserId), nameof(FriendRequest.Status), nameof(FriendRequest.CreatedDate), nameof(FriendRequest.Id)],
            [false, false, true, true]);
        AssertIndex<Friendship>(model, "IX_Friendship_User_Cursor",
            [nameof(Friendship.UserId), nameof(Friendship.CreatedDate), nameof(Friendship.Id)],
            [false, true, true]);
        AssertIndex<Follow>(model, "IX_Follow_Follower_Cursor",
            [nameof(Follow.FollowerUserId), nameof(Follow.CreatedDate), nameof(Follow.Id)],
            [false, true, true]);
        AssertIndex<Follow>(model, "IX_Follow_Followed_Cursor",
            [nameof(Follow.FollowedUserId), nameof(Follow.CreatedDate), nameof(Follow.Id)],
            [false, true, true]);
        AssertIndex<CategoryFollow>(model, "IX_CategoryFollow_User_Cursor",
            [nameof(CategoryFollow.UserId), nameof(CategoryFollow.CreatedDate), nameof(CategoryFollow.Id)],
            [false, true, true]);
        AssertIndex<UserBlock>(model, "IX_UserBlock_Blocker_Cursor",
            [nameof(UserBlock.BlockerUserId), nameof(UserBlock.CreatedDate), nameof(UserBlock.Id)],
            [false, true, true]);
    }

    private static FollowingContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                "Server=localhost;Database=following_cursor_query_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        return new FollowingContext(options);
    }

    private static Follow CreateFollow(
        int rowId,
        User follower,
        User followed,
        DateTime createdDate)
    {
        var follow = Follow.Create(follower, followed, existing: null);
        typeof(BaseEntity).GetField(
                "_id",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(follow, rowId);
        typeof(BaseEntity).GetField(
                "_createdDate",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(follow, createdDate);
        return follow;
    }

    private static DateTime Utc(int hour) =>
        new(2026, 7, 17, hour, 0, 0, DateTimeKind.Utc);

    private static void AssertIndex<TEntity>(
        IModel model,
        string databaseName,
        string[] properties,
        bool[] descending)
        where TEntity : class
    {
        var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(TEntity)));
        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == databaseName);
        Assert.Equal(properties, index.Properties.Select(property => property.Name));
        Assert.Equal(descending, index.IsDescending);
    }
}
