using Following.Application.Providers.Categories;
using Following.Application.Providers.Privacy;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Domain.AggregatesModel;
using Following.Domain.Dtos;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using Following.Infra.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerContracts.Categories;
using MessageBrokerContracts.Following;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StarterEntities;
using Xunit;

namespace Following.UnitTests;

[Collection(FollowingCursorMariaDbCollection.Name)]
public sealed class FollowingUserErasureMariaDbIntegrationTests
{
    private const string ConnectionEnvironment = "FOLLOWING_CURSOR_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Tombstone_fences_races_and_bounded_processor_erases_replays_and_outboxes()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        await using var services = BuildServices(connectionString);
        var userId = Random.Shared.Next(6_100_000, 6_800_000);
        var outgoingOwner = userId + 1;
        var incomingFollower = userId + 2;
        var friendId = userId + 3;
        var requestRecipient = userId + 4;
        var blockedId = userId + 5;
        var subscriberId = userId + 6;
        var categoryId = userId + 7;
        var requestId = Guid.NewGuid();

        await using (var seedScope = services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FollowingContext>();
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var repository = new FollowingRepository(context);
            var user = new User(userId, "erased-user");
            var outgoing = Follow.Create(user, new User(outgoingOwner, "outgoing-owner"), null);
            var incoming = Follow.Create(new User(incomingFollower, "incoming-follower"), user, null);
            var friendships = Friendship.CreatePair(user, new User(friendId, "friend"), false);
            var request = FriendRequest.Create(
                user,
                new User(requestRecipient, "recipient"),
                existingRequest: null,
                alreadyFriends: false);
            var block = UserBlock.Create(userId, blockedId, existing: null);
            var subscription = FriendshipSubscriptionFeature.Create(
                userId,
                subscriberId,
                $"privacy-{Guid.NewGuid():N}",
                new SubscriptionFeatureUpdateDomainDto { EndDate = Utc(0).AddDays(10) },
                Utc(0));
            var category = CategoryFollow.Create(
                user,
                categoryId,
                new CategoryFollowDomainDto { CategoryName = "privacy-category" },
                existing: null);

            context.AddRange(outgoing, incoming, request, block, subscription, category);
            context.Friendships.AddRange(friendships);
            await IncrementSeedAnalyticsAsync(
                repository,
                userId,
                outgoingOwner,
                incomingFollower,
                friendId,
                requestRecipient,
                categoryId,
                TestContext.Current.CancellationToken);
            context.RelationshipEventStreams.AddRange(
                Stream(outgoingOwner, userId, RelationshipKinds.Follow),
                Stream(userId, friendId, RelationshipKinds.Friend),
                Stream(userId, blockedId, RelationshipKinds.Block, RelationshipStates.Blocked));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var mutationScope = services.CreateAsyncScope();
        var mutationRepository = mutationScope.ServiceProvider.GetRequiredService<IFollowingRepository>();
        await using var inFlight = await mutationRepository.AcquireRelationshipPairMutation(
            userId,
            outgoingOwner,
            TestContext.Current.CancellationToken);
        await using var scheduleScope = services.CreateAsyncScope();
        var scheduleTask = scheduleScope.ServiceProvider
            .GetRequiredService<IFollowingUserErasureScheduler>()
            .ScheduleAsync(
                Request(requestId, userId, version: 1),
                TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(scheduleTask.IsCompleted);
        await inFlight.CommitAsync(TestContext.Current.CancellationToken);
        Assert.True((await scheduleTask).Scheduled);

        await using (var hiddenScope = services.CreateAsyncScope())
        {
            var context = hiddenScope.ServiceProvider.GetRequiredService<FollowingContext>();
            Assert.False(await context.Follows.AsNoTracking().AnyAsync(
                item => item.FollowerUserId == userId || item.FollowedUserId == userId,
                TestContext.Current.CancellationToken));
            var repository = hiddenScope.ServiceProvider.GetRequiredService<IFollowingRepository>();
            await using var rejectedMutation = await repository.AcquireRelationshipPairMutation(
                userId,
                incomingFollower,
                TestContext.Current.CancellationToken);
            var rejected = await Assert.ThrowsAsync<FollowingException>(() =>
                repository.EnsureUsersNotErased(
                    [userId, incomingFollower],
                    TestContext.Current.CancellationToken));
            Assert.Equal(410, rejected.StatusCode);
        }

        FollowingUserErasureCycleResult cycle = null!;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using var processorScope = services.CreateAsyncScope();
            cycle = await processorScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureProcessor>()
                .ProcessCycleAsync("integration-worker", TestContext.Current.CancellationToken);
            if (cycle.Completed)
                break;
        }
        Assert.True(cycle.Completed);

        await using (var verificationScope = services.CreateAsyncScope())
        {
            var context = verificationScope.ServiceProvider.GetRequiredService<FollowingContext>();
            Assert.False(await context.Follows.IgnoreQueryFilters().AnyAsync(
                item => item.FollowerUserId == userId || item.FollowedUserId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.Friendships.IgnoreQueryFilters().AnyAsync(
                item => item.UserId == userId || item.FriendUserId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.FriendRequests.IgnoreQueryFilters().AnyAsync(
                item => item.SenderUserId == userId || item.RecipientUserId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.UserBlocks.IgnoreQueryFilters().AnyAsync(
                item => item.BlockerUserId == userId || item.BlockedUserId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.FriendsSubscriptionsFeature.IgnoreQueryFilters().AnyAsync(
                item => item.OwnerId == userId || item.RequesterId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.CategoryFollows.IgnoreQueryFilters().AnyAsync(
                item => item.UserId == userId,
                TestContext.Current.CancellationToken));
            Assert.False(await context.UserRelationsAnalytics.IgnoreQueryFilters().AnyAsync(
                item => item.Id == userId,
                TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS `Value`
                    FROM `information_schema`.`TABLES`
                    WHERE `TABLE_SCHEMA` = DATABASE()
                      AND `TABLE_NAME` = 'UserRelationAnalyticsLegacy'
                    """)
                .SingleAsync(TestContext.Current.CancellationToken));
            Assert.All(
                await context.RelationshipEventStreams.IgnoreQueryFilters()
                    .Where(item => item.OwnerUserId == userId || item.ViewerUserId == userId)
                    .ToArrayAsync(TestContext.Current.CancellationToken),
                stream => Assert.Equal(RelationshipStates.Revoked, stream.CurrentState));

            var tombstone = await context.UserErasures.AsNoTracking().SingleAsync(
                item => item.UserId == userId,
                TestContext.Current.CancellationToken);
            Assert.Equal(FollowingUserErasureStatus.Completed, tombstone.Status);
            Assert.Equal(1, tombstone.ErasureVersion);
            Assert.NotNull(tombstone.CompletionEventId);

            var reads = new FollowingReadRepository(context);
            Assert.Equal(0, (await reads.GetUserAnalytics(
                outgoingOwner, TestContext.Current.CancellationToken)).TotalFollowers);
            Assert.Equal(0, (await reads.GetUserAnalytics(
                incomingFollower, TestContext.Current.CancellationToken)).TotalFollowing);
            Assert.Equal(0, (await reads.GetUserAnalytics(
                friendId, TestContext.Current.CancellationToken)).TotalFriends);
            Assert.Equal(0, (await reads.GetUserAnalytics(
                requestRecipient, TestContext.Current.CancellationToken)).TotalFriendsRequests);
            Assert.True(await context.Set<OutboxMessage>().AsNoTracking().AnyAsync(
                item => item.Body.Contains(UserErasureOutcomes.Completed),
                TestContext.Current.CancellationToken));
        }

        long outboxBeforeReplay;
        await using (var replayScope = services.CreateAsyncScope())
        {
            var context = replayScope.ServiceProvider.GetRequiredService<FollowingContext>();
            outboxBeforeReplay = await context.Set<OutboxMessage>().LongCountAsync(
                TestContext.Current.CancellationToken);
            var replay = await replayScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureScheduler>()
                .ScheduleAsync(
                    Request(requestId, userId, version: 1),
                    TestContext.Current.CancellationToken);
            Assert.False(replay.Scheduled);
            Assert.False(replay.Superseded);
            Assert.Equal(outboxBeforeReplay, await context.Set<OutboxMessage>().LongCountAsync(
                TestContext.Current.CancellationToken));
        }

        var newerRequest = Guid.NewGuid();
        await using (var newerScope = services.CreateAsyncScope())
        {
            var scheduled = await newerScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureScheduler>()
                .ScheduleAsync(
                    Request(newerRequest, userId, version: 2),
                    TestContext.Current.CancellationToken);
            Assert.True(scheduled.Scheduled);
        }
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var processorScope = services.CreateAsyncScope();
            cycle = await processorScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureProcessor>()
                .ProcessCycleAsync("integration-worker", TestContext.Current.CancellationToken);
            if (cycle.Completed)
                break;
        }
        Assert.True(cycle.Completed);

        await using (var staleScope = services.CreateAsyncScope())
        {
            var stale = await staleScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureScheduler>()
                .ScheduleAsync(
                    Request(Guid.NewGuid(), userId, version: 1),
                    TestContext.Current.CancellationToken);
            Assert.True(stale.Superseded);
            var tombstone = await staleScope.ServiceProvider
                .GetRequiredService<FollowingContext>()
                .UserErasures.AsNoTracking()
                .SingleAsync(item => item.UserId == userId, TestContext.Current.CancellationToken);
            Assert.Equal(2, tombstone.ErasureVersion);
            Assert.Equal(FollowingUserErasureStatus.Completed, tombstone.Status);
        }
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Failed_batch_rolls_back_and_expired_lease_retries_without_count_loss()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        await using var services = BuildServices(connectionString);
        var userId = Random.Shared.Next(7_100_000, 7_800_000);
        var ownerId = userId + 1;
        var requestId = Guid.NewGuid();

        await using (var seedScope = services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<FollowingContext>();
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var repository = new FollowingRepository(context);
            context.Follows.Add(Follow.Create(
                new User(userId, "erased"),
                new User(ownerId, "owner"),
                existing: null));
            (await repository.GetOrCreateAnalytics(
                userId, ownerId, TestContext.Current.CancellationToken)).IncrementFollowing();
            (await repository.GetOrCreateAnalytics(
                ownerId, userId, TestContext.Current.CancellationToken)).IncrementFollowers();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            await seedScope.ServiceProvider.GetRequiredService<IFollowingUserErasureScheduler>()
                .ScheduleAsync(
                    Request(requestId, userId, version: 1),
                    TestContext.Current.CancellationToken);
        }

        await using (var failedScope = services.CreateAsyncScope())
        {
            var processor = new FollowingUserErasureProcessor(
                failedScope.ServiceProvider.GetRequiredService<FollowingContext>(),
                new ThrowingRelationshipEvents(),
                failedScope.ServiceProvider.GetRequiredService<
                    Following.Application.Providers.Categories.CategoriesProvider>(),
                failedScope.ServiceProvider.GetRequiredService<IIntegrationMessagePublisher>(),
                failedScope.ServiceProvider.GetRequiredService<IClock>(),
                failedScope.ServiceProvider.GetRequiredService<IOptions<FollowingUserErasureOptions>>(),
                failedScope.ServiceProvider.GetRequiredService<FollowingUserErasureMetrics>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessCycleAsync(
                "failing-worker",
                TestContext.Current.CancellationToken));
        }

        await using (var rollbackScope = services.CreateAsyncScope())
        {
            var context = rollbackScope.ServiceProvider.GetRequiredService<FollowingContext>();
            Assert.True(await context.Follows.IgnoreQueryFilters().AnyAsync(
                item => item.FollowerUserId == userId && item.FollowedUserId == ownerId,
                TestContext.Current.CancellationToken));
            var tombstone = await context.UserErasures.AsNoTracking().SingleAsync(
                item => item.UserId == userId,
                TestContext.Current.CancellationToken);
            Assert.Equal(FollowingUserErasureStage.FollowsAsFollower, tombstone.Stage);
            Assert.Equal(0, tombstone.Cursor);
            Assert.Equal(FollowingUserErasureStatus.Processing, tombstone.Status);
        }

        var retryClock = services.GetRequiredService<MutableClock>();
        retryClock.Advance(TimeSpan.FromSeconds(31));
        FollowingUserErasureCycleResult cycle = null!;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await using var retryScope = services.CreateAsyncScope();
            cycle = await retryScope.ServiceProvider
                .GetRequiredService<IFollowingUserErasureProcessor>()
                .ProcessCycleAsync("retry-worker", TestContext.Current.CancellationToken);
            if (cycle.Completed)
                break;
            // A hard time-budget cancellation deliberately leaves the durable lease in place;
            // advance the deterministic integration clock to exercise expiry/reclaim again.
            retryClock.Advance(TimeSpan.FromSeconds(31));
        }
        Assert.True(cycle.Completed);

        await using var verificationScope = services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<FollowingContext>();
        Assert.False(await verification.Follows.IgnoreQueryFilters().AnyAsync(
            item => item.FollowerUserId == userId && item.FollowedUserId == ownerId,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, (await new FollowingReadRepository(verification).GetUserAnalytics(
            ownerId,
            TestContext.Current.CancellationToken)).TotalFollowers);
        Assert.Equal(FollowingUserErasureStatus.Completed, await verification.UserErasures
            .Where(item => item.UserId == userId)
            .Select(item => item.Status)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    private static async Task IncrementSeedAnalyticsAsync(
        IFollowingRepository repository,
        int userId,
        int outgoingOwner,
        int incomingFollower,
        int friendId,
        int requestRecipient,
        int categoryId,
        CancellationToken cancellationToken)
    {
        (await repository.GetOrCreateAnalytics(userId, outgoingOwner, cancellationToken))
            .IncrementFollowing();
        (await repository.GetOrCreateAnalytics(outgoingOwner, userId, cancellationToken))
            .IncrementFollowers();
        (await repository.GetOrCreateAnalytics(incomingFollower, userId, cancellationToken))
            .IncrementFollowing();
        (await repository.GetOrCreateAnalytics(userId, incomingFollower, cancellationToken))
            .IncrementFollowers();
        (await repository.GetOrCreateAnalytics(userId, friendId, cancellationToken))
            .IncrementFriends();
        (await repository.GetOrCreateAnalytics(friendId, userId, cancellationToken))
            .IncrementFriends();
        (await repository.GetOrCreateAnalytics(requestRecipient, userId, cancellationToken))
            .IncrementFriendRequests();
        (await repository.GetOrCreateAnalytics(userId, categoryId, cancellationToken))
            .IncrementCategoryFollowings();
    }

    private static FollowingUserErasureRequest Request(Guid requestId, int userId, long version) =>
        new(Guid.NewGuid(), requestId, userId, version, Utc(0));

    private static RelationshipEventStream Stream(
        int owner,
        int viewer,
        string kind,
        string state = RelationshipStates.Active) =>
        RelationshipEventStream.Create(
            Guid.NewGuid(), Guid.NewGuid(), owner, viewer, kind, state, Utc(0));

    private static DateTime Utc(int minute) =>
        new(2026, 7, 17, 12, minute, 0, DateTimeKind.Utc);

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FollowingContext>(options => options.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(10, 6, 11))));
        services.AddSingleton(new MutableClock(Utc(0)));
        services.AddSingleton<IClock>(provider => provider.GetRequiredService<MutableClock>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<FollowingUserErasureOptions>>(Options.Create(new FollowingUserErasureOptions
        {
            BatchSize = 1,
            MaxBatchesPerCycle = 32,
            CycleTimeBudgetMilliseconds = 5_000,
            LeaseSeconds = 30,
            ActiveDelayMilliseconds = 10,
        }));
        services.AddSingleton<FollowingUserErasureMetrics>();
        services.AddScoped<IFollowingRepository, FollowingRepository>();
        services.AddScoped<IFollowingUserErasureScheduler, FollowingUserErasureScheduler>();
        services.AddScoped<IFollowingUserErasureProcessor, FollowingUserErasureProcessor>();
        services.AddScoped<IRelationshipEventWriter, RelationshipEventWriter>();
        services.AddScoped<IIntegrationMessagePublisher, MassTransitMessagePublisher>();
        services.AddScoped<ICategories, OutboxCategories>();
        services.AddScoped<Following.Application.Providers.Categories.CategoriesProvider>();
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<FollowingContext>(outbox =>
            {
                outbox.UseMySql();
                outbox.UseBusOutbox();
            });
            registration.UsingRabbitMq((_, bus) =>
            {
                bus.Host("localhost", 5672, "following-erasure-tests", host =>
                {
                    host.Username("guest");
                    host.Password("guest");
                });
            });
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; private set; } = utcNow;
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class ThrowingRelationshipEvents : IRelationshipEventWriter
    {
        public Task<RelationshipStateChanged> StageAsync(
            RelationshipTransition transition,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected relationship outbox failure.");
    }

    private sealed class OutboxCategories(
        IIntegrationMessagePublisher publisher,
        IClock clock) : ICategories
    {
        public Task<int> GetIdByName(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task UpdateFollowAsync(
            int[] categoryIds,
            string[] categoryNames,
            bool isFollowing,
            CancellationToken cancellationToken)
        {
            var eventId = Guid.NewGuid();
            await publisher.PublishAsync(
                new CategoryFollowsChanged
                {
                    EventId = eventId,
                    OccurredAtUtc = clock.UtcNow,
                    CategoryIds = categoryIds,
                    CategoryNames = categoryNames,
                    IsFollowing = isFollowing,
                },
                eventId,
                cancellationToken);
        }
    }
}
