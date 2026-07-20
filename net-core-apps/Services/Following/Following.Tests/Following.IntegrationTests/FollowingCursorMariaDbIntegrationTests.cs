using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using StarterEntities;
using System.Reflection;
using Xunit;

namespace Following.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FollowingCursorMariaDbCollection
{
    public const string Name = "Following isolated MariaDB cursor";
}

[Collection(FollowingCursorMariaDbCollection.Name)]
public sealed class FollowingCursorMariaDbIntegrationTests
{
    private const string ConnectionEnvironment =
        "FOLLOWING_CURSOR_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Real_keyset_query_is_stable_under_a_newer_concurrent_insert()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        await using var context = new FollowingContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var ownerId = Random.Shared.Next(100_000, 1_000_000);
        var owner = new User(ownerId, $"cursor-owner-{ownerId}");
        var oldest = CreateFollow(owner, ownerId + 1, Utc(10));
        var middle = CreateFollow(owner, ownerId + 2, Utc(11));
        var newest = CreateFollow(owner, ownerId + 3, Utc(12));
        context.Follows.AddRange(oldest, middle, newest);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var repository = new FollowingReadRepository(context);
        var first = await repository.ListFollowsCursor(
            ownerId,
            following: true,
            new FollowingCursorWindow(2, null!),
            TestContext.Current.CancellationToken);

        Assert.True(first.HasMore);
        Assert.Equal(
            [newest.Id, middle.Id],
            first.Items.Select(item => item.RowId));

        var concurrent = CreateFollow(owner, ownerId + 4, Utc(13));
        context.Follows.Add(concurrent);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var second = await repository.ListFollowsCursor(
            ownerId,
            following: true,
            new FollowingCursorWindow(2, first.NextPosition),
            TestContext.Current.CancellationToken);

        Assert.False(second.HasMore);
        Assert.Equal([oldest.Id], second.Items.Select(item => item.RowId));
        Assert.DoesNotContain(concurrent.Id, second.Items.Select(item => item.RowId));
        Assert.Empty(first.Items.Select(item => item.RowId)
            .Intersect(second.Items.Select(item => item.RowId)));

        var nextPosition = Assert.IsType<FollowingCursorPosition>(first.NextPosition);
        await AssertCursorPlanAsync(
            connectionString,
            ownerId,
            nextPosition,
            TestContext.Current.CancellationToken);
    }

    private static Follow CreateFollow(User owner, int followedId, DateTime createdDate)
    {
        var follow = Follow.Create(
            owner,
            new User(followedId, $"cursor-followed-{followedId}"),
            existing: null);
        typeof(BaseEntity).GetField(
                "_createdDate",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(follow, createdDate);
        return follow;
    }

    private static DateTime Utc(int hour) =>
        new(2026, 7, 17, hour, 0, 0, DateTimeKind.Utc);

    private static async Task AssertCursorPlanAsync(
        string connectionString,
        int ownerId,
        FollowingCursorPosition after,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            """
            EXPLAIN SELECT `Id`
            FROM `Follow`
            WHERE `User_Id` = @ownerId
              AND (`CreatedDate` < @createdAt
                   OR (`CreatedDate` = @createdAt AND `Id` < @id))
            ORDER BY `CreatedDate` DESC, `Id` DESC
            LIMIT 51
            """,
            connection);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@createdAt", after.CreatedDateUtc);
        command.Parameters.AddWithValue("@id", after.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        var chosenKey = reader.IsDBNull(reader.GetOrdinal("key"))
            ? string.Empty
            : reader.GetString(reader.GetOrdinal("key"));
        Assert.Equal("IX_Follow_Follower_Cursor", chosenKey);
    }
}
