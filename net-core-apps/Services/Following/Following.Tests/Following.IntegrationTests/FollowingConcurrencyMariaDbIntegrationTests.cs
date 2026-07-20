using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using StarterEntities;
using Xunit;

namespace Following.UnitTests;

[Collection(FollowingCursorMariaDbCollection.Name)]
public sealed class FollowingConcurrencyMariaDbIntegrationTests
{
    private const string ConnectionEnvironment =
        "FOLLOWING_CURSOR_MARIADB_TEST_CONNECTION";
    private const int ContenderCount = 100;

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task One_hundred_concurrent_identical_follows_commit_exactly_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = CreateOptions(connectionString);

        await using (var migrationContext = new FollowingContext(options))
            await migrationContext.Database.MigrateAsync(cancellationToken);

        var followerUserId = Random.Shared.Next(10_000_000, 100_000_000);
        var followedUserId = followerUserId + 1;
        using var ready = new CountdownEvent(ContenderCount);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateFollow()
        {
            ready.Signal();
            await release.Task.WaitAsync(cancellationToken);

            await using var context = new FollowingContext(options);
            var repository = new FollowingRepository(context);
            await using var mutation = await repository.AcquireRelationshipPairMutation(
                followerUserId,
                followedUserId,
                cancellationToken);

            var existing = await repository.GetFollow(
                followerUserId,
                followedUserId,
                cancellationToken);
            try
            {
                var follow = Follow.Create(
                    new User(followerUserId, $"race-follower-{followerUserId}"),
                    new User(followedUserId, $"race-followed-{followedUserId}"),
                    existing);
                await repository.Add(follow, cancellationToken);
                await repository.SaveChanges(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
                return true;
            }
            catch (FollowingException)
            {
                return false;
            }
        }

        var contenders = Enumerable.Range(0, ContenderCount)
            .Select(_ => TryCreateFollow())
            .ToArray();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10), cancellationToken));
        release.SetResult();

        try
        {
            var outcomes = await Task.WhenAll(contenders)
                .WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);

            Assert.Single(outcomes, committed => committed);
            Assert.Equal(ContenderCount - 1, outcomes.Count(committed => !committed));

            await using var assertionContext = new FollowingContext(options);
            var storedCount = await assertionContext.Follows
                .IgnoreQueryFilters()
                .CountAsync(
                    item => item.FollowerUserId == followerUserId
                            && item.FollowedUserId == followedUserId,
                    cancellationToken);
            Assert.Equal(1, storedCount);
        }
        finally
        {
            using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var cleanupContext = new FollowingContext(options);
            await cleanupContext.Follows
                .IgnoreQueryFilters()
                .Where(item => item.FollowerUserId == followerUserId
                               && item.FollowedUserId == followedUserId)
                .ExecuteDeleteAsync(cleanupBudget.Token);
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `RelationshipPairGuard` WHERE `PairMinUserId` = {followerUserId} AND `PairMaxUserId` = {followedUserId};",
                cleanupBudget.Token);
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `UserMutationGuard` WHERE `UserId` IN ({followerUserId}, {followedUserId});",
                cleanupBudget.Token);
        }
    }

    private static DbContextOptions<FollowingContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
}
