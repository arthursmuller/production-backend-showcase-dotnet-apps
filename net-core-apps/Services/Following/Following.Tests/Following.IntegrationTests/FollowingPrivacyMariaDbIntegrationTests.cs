using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Application.Features.Followings;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Infra.Persistence;
using IdentityProvider;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerContracts.Following;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarterEntities;
using Xunit;

namespace Following.UnitTests;

[Collection(FollowingCursorMariaDbCollection.Name)]
public sealed class FollowingPrivacyMariaDbIntegrationTests
{
    private const string ConnectionEnvironment =
        "FOLLOWING_CURSOR_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Pair_guard_serializes_block_vs_relationship_and_trigger_fails_closed()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        var blockerId = Random.Shared.Next(1_100_000, 1_900_000);
        var blockedId = blockerId + 1;

        await using var blockContext = new FollowingContext(options);
        await blockContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var blockRepository = new FollowingRepository(blockContext);
        await using var blockMutation = await blockRepository.AcquireRelationshipPairMutation(
            blockerId,
            blockedId,
            TestContext.Current.CancellationToken);

        await using var relationshipContext = new FollowingContext(options);
        var relationshipRepository = new FollowingRepository(relationshipContext);
        var waitingMutation = relationshipRepository.AcquireRelationshipPairMutation(
            blockedId,
            blockerId,
            TestContext.Current.CancellationToken);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(waitingMutation.IsCompleted);

        await blockRepository.Add(
            UserBlock.Create(blockerId, blockedId, existing: null),
            TestContext.Current.CancellationToken);
        await blockRepository.SaveChanges(TestContext.Current.CancellationToken);
        await blockMutation.CommitAsync(TestContext.Current.CancellationToken);

        await using var relationshipMutation = await waitingMutation;
        Assert.True(await relationshipRepository.IsBlockedEitherDirection(
            blockerId,
            blockedId,
            TestContext.Current.CancellationToken));

        await relationshipRepository.Add(
            Follow.Create(
                new User(blockerId, "blocked-follower"),
                new User(blockedId, "blocked-owner"),
                existing: null),
            TestContext.Current.CancellationToken);
        var rejection = await Assert.ThrowsAsync<FollowingException>(() =>
            relationshipRepository.SaveChanges(TestContext.Current.CancellationToken));
        Assert.Equal(409, rejection.StatusCode);

        await using var verification = new FollowingContext(options);
        Assert.False(await verification.Follows.AsNoTracking().AnyAsync(
            item => item.FollowerUserId == blockerId && item.FollowedUserId == blockedId,
            TestContext.Current.CancellationToken));
        Assert.True(await verification.UserBlocks.AsNoTracking().AnyAsync(
            item => item.BlockerUserId == blockerId && item.BlockedUserId == blockedId,
            TestContext.Current.CancellationToken));
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Snapshot_boundary_and_timestamp_stay_fixed_while_new_streams_arrive()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        await using var context = new FollowingContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var ownerId = Random.Shared.Next(2_100_000, 2_900_000);
        context.RelationshipEventStreams.AddRange(
            Stream(ownerId, ownerId + 1, Utc(1)),
            Stream(ownerId, ownerId + 2, Utc(2)),
            Stream(ownerId, ownerId + 3, Utc(3)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reader = new RelationshipSnapshotReader(context);
        var first = await reader.ReadPageAsync(
            afterCursor: 0,
            pageSize: 1,
            snapshotHighWatermark: null,
            TestContext.Current.CancellationToken);
        Assert.True(first.HasMore);

        context.RelationshipEventStreams.Add(Stream(ownerId, ownerId + 4, Utc(4)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var second = await reader.ReadPageAsync(
            first.NextCursor,
            pageSize: 10,
            first.SnapshotHighWatermark,
            TestContext.Current.CancellationToken);

        Assert.False(second.HasMore);
        Assert.Equal(first.SnapshotHighWatermark, second.SnapshotHighWatermark);
        Assert.Equal(first.SnapshotAsOfUtc, second.SnapshotAsOfUtc);
        var fixedSnapshot = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(3, fixedSnapshot.Count(item => item.OwnerUserId == ownerId));
        Assert.DoesNotContain(
            fixedSnapshot,
            item => item.OwnerUserId == ownerId && item.ViewerUserId == ownerId + 4);
        Assert.All(second.Items, item => Assert.True(item.Cursor <= first.SnapshotHighWatermark));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(
            first.NextCursor,
            10,
            snapshotHighWatermark: null,
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(
            0,
            10,
            first.SnapshotHighWatermark,
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadPageAsync(
            0,
            10,
            first.SnapshotHighWatermark + 1_000,
            TestContext.Current.CancellationToken));
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Concurrent_directional_block_and_unblock_requests_are_idempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<FollowingContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        var blockerId = Random.Shared.Next(3_100_000, 3_900_000);
        var blockedId = blockerId + 1;

        await using var firstBlockContext = new FollowingContext(options);
        await firstBlockContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await using var secondBlockContext = new FollowingContext(options);
        var firstBlock = new BlockUserCommand(
            new FollowingRepository(firstBlockContext),
            new StubIdentity(blockerId),
            new NoopRelationshipEvents());
        var secondBlock = new BlockUserCommand(
            new FollowingRepository(secondBlockContext),
            new StubIdentity(blockerId),
            new NoopRelationshipEvents());

        var blockResults = await Task.WhenAll(
            firstBlock.Handle(blockedId, TestContext.Current.CancellationToken),
            secondBlock.Handle(blockedId, TestContext.Current.CancellationToken));
        Assert.Equal([false, true], blockResults.Order().ToArray());

        await using var firstUnblockContext = new FollowingContext(options);
        await using var secondUnblockContext = new FollowingContext(options);
        var firstUnblock = new UnblockUserCommand(
            new FollowingRepository(firstUnblockContext),
            new StubIdentity(blockerId),
            new NoopRelationshipEvents());
        var secondUnblock = new UnblockUserCommand(
            new FollowingRepository(secondUnblockContext),
            new StubIdentity(blockerId),
            new NoopRelationshipEvents());

        var unblockResults = await Task.WhenAll(
            firstUnblock.Handle(blockedId, TestContext.Current.CancellationToken),
            secondUnblock.Handle(blockedId, TestContext.Current.CancellationToken));
        Assert.Equal([false, true], unblockResults.Order().ToArray());

        await using var verification = new FollowingContext(options);
        Assert.False(await verification.UserBlocks.AsNoTracking().AnyAsync(
            item => item.BlockerUserId == blockerId && item.BlockedUserId == blockedId,
            TestContext.Current.CancellationToken));
        Assert.False(await verification.Follows.AsNoTracking().AnyAsync(
            item => (item.FollowerUserId == blockerId && item.FollowedUserId == blockedId)
                || (item.FollowerUserId == blockedId && item.FollowedUserId == blockerId),
            TestContext.Current.CancellationToken));
        Assert.False(await verification.Friendships.AsNoTracking().AnyAsync(
            item => (item.UserId == blockerId && item.FriendUserId == blockedId)
                || (item.UserId == blockedId && item.FriendUserId == blockerId),
            TestContext.Current.CancellationToken));
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Block_authority_streams_and_bus_outbox_commit_in_one_unit_of_work()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        await using var services = BuildOutboxServices(connectionString);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FollowingContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var outboxBefore = await context.Set<OutboxMessage>().LongCountAsync(
            TestContext.Current.CancellationToken);
        var blockerId = Random.Shared.Next(4_100_000, 4_900_000);
        var blockedId = blockerId + 1;
        var command = new BlockUserCommand(
            new FollowingRepository(context),
            new StubIdentity(blockerId),
            scope.ServiceProvider.GetRequiredService<IRelationshipEventWriter>());

        Assert.True(await command.Handle(
            blockedId,
            TestContext.Current.CancellationToken));

        Assert.True(await context.UserBlocks.AsNoTracking().AnyAsync(
            item => item.BlockerUserId == blockerId && item.BlockedUserId == blockedId,
            TestContext.Current.CancellationToken));
        var streams = await context.RelationshipEventStreams.AsNoTracking()
            .Where(item =>
                (item.OwnerUserId == blockerId && item.ViewerUserId == blockedId)
                || (item.OwnerUserId == blockedId && item.ViewerUserId == blockerId))
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, streams.Length);
        Assert.Equal(2, streams.Count(item =>
            item.RelationshipKind == RelationshipKinds.Follow
            && item.CurrentState == RelationshipStates.Revoked));
        Assert.Equal(2, streams.Count(item =>
            item.RelationshipKind == RelationshipKinds.Friend
            && item.CurrentState == RelationshipStates.Revoked));
        Assert.Single(streams, item =>
            item.RelationshipKind == RelationshipKinds.Block
            && item.CurrentState == RelationshipStates.Blocked);
        Assert.Equal(
            outboxBefore + 5,
            await context.Set<OutboxMessage>().LongCountAsync(
                TestContext.Current.CancellationToken));
    }

    private static RelationshipEventStream Stream(
        int ownerUserId,
        int viewerUserId,
        DateTime occurredAtUtc) =>
        RelationshipEventStream.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ownerUserId,
            viewerUserId,
            "friend",
            "active",
            occurredAtUtc);

    private static DateTime Utc(int minute) =>
        new(2026, 7, 17, 12, minute, 0, DateTimeKind.Utc);

    private static ServiceProvider BuildOutboxServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FollowingContext>(options => options.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(10, 6, 11))));
        services.AddSingleton<IClock>(new FixedClock(Utc(0)));
        services.AddScoped<IRelationshipEventWriter, RelationshipEventWriter>();
        services.AddScoped<IIntegrationMessagePublisher, MassTransitMessagePublisher>();
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<FollowingContext>(outbox =>
            {
                outbox.UseMySql();
                outbox.UseBusOutbox();
            });
            registration.UsingRabbitMq((_, bus) =>
            {
                bus.Host("localhost", 5672, "following-privacy-outbox-tests", host =>
                {
                    host.Username("guest");
                    host.Password("guest");
                });
            });
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class StubIdentity(int userId) : IIdentityService
    {
        public AuthUser User { get; } = new(userId, $"user-{userId}");
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class NoopRelationshipEvents : IRelationshipEventWriter
    {
        public Task<RelationshipStateChanged> StageAsync(
            RelationshipTransition transition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RelationshipStateChanged
            {
                EventId = Guid.NewGuid(),
                StreamId = Guid.NewGuid(),
                StreamVersion = 1,
                OwnerUserId = transition.OwnerUserId,
                ViewerUserId = transition.ViewerUserId,
                RelationshipKind = transition.RelationshipKind,
                State = transition.State,
                OccurredAtUtc = DateTime.UtcNow,
                ReasonCode = transition.ReasonCode,
            });
        }
    }
}
