using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using Inspacy.Caching;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class FeedDeliveryIntegrationTests
{
    private static readonly MariaDbServerVersion ServerVersion = new(new Version(10, 6, 0));
    private static readonly DateTime Now =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Integration")]
    public Task Expired_fanout_lease_resumes_in_500_row_pages_without_duplicates_and_revokes_in_priority_pages() =>
        InIsolatedDatabase("feed_delivery", async (connectionString, cancellationToken) =>
        {
            var dbOptions = ContextOptions(connectionString);
            await using (var seed = new FeedContext(dbOptions))
            {
                await seed.Database.MigrateAsync(cancellationToken);
                await seed.FeedAudienceBootstrapStates.AddAsync(
                    CompleteBootstrap(Now),
                    cancellationToken);
                var activity = FeedActivity.Create(
                    "post",
                    "fanout-1201",
                    "created",
                    actorUserId: 10,
                    FeedAudiencePolicies.Followers,
                    Now,
                    Now,
                    sourceStreamId: Guid.NewGuid(),
                    eventId: Guid.NewGuid(),
                    deliveryMode: FeedDeliveryModes.Push);
                await seed.FeedActivities.AddAsync(activity, cancellationToken);
                for (var viewer = 20; viewer < 1_221; viewer++)
                    await AddActiveFollowAsync(seed, 10, viewer, cancellationToken);
                var work = FanoutWorkItem.Create("fanout:expired-lease", activity, Now);
                work.Lease("dead-worker", Now.AddMinutes(-1), TimeSpan.FromSeconds(10));
                await seed.FanoutWorkItems.AddAsync(work, cancellationToken);
                await seed.SaveChangesAsync(cancellationToken);
            }

            var options = Options.Create(TestOptions());
            await using (var context = new FeedContext(dbOptions))
            {
                var processor = new FeedFanoutProcessor(
                    context,
                    new FeedUserErasureAuthority(context),
                    options,
                    AudienceOptions(),
                    new FeedTelemetry());
                Assert.Equal(500, await processor.ProcessBatchAsync("worker-a", Now, cancellationToken));
                Assert.Equal(500, await processor.ProcessBatchAsync("worker-a", Now.AddSeconds(1), cancellationToken));
                Assert.Equal(201, await processor.ProcessBatchAsync("worker-a", Now.AddSeconds(2), cancellationToken));
                Assert.Equal(0, await processor.ProcessBatchAsync("worker-a", Now.AddSeconds(3), cancellationToken));

                Assert.Equal(1_201, await context.FeedEntries.CountAsync(cancellationToken));
                Assert.Equal(
                    1_201,
                    await context.FeedEntries
                        .Select(entry => new { entry.RecipientUserId, entry.ActivityId })
                        .Distinct()
                        .CountAsync(cancellationToken));
                var fanout = await context.FanoutWorkItems.SingleAsync(cancellationToken);
                Assert.Equal(FeedWorkStates.Completed, fanout.State);
                Assert.Equal(1_220, fanout.NextViewerUserId);
                Assert.Equal(1_220, fanout.AudienceHighWatermarkUserId);

                var activity = await context.FeedActivities.SingleAsync(cancellationToken);
                await context.FeedRevocationWorkItems.AddAsync(
                    FeedRevocationWorkItem.ForActivity(
                        "delete:fanout-1201",
                        activity,
                        Now.AddSeconds(4),
                        Now.AddSeconds(4)),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                var revocations = new FeedRevocationProcessor(
                    context,
                    new FeedUserErasureAuthority(context),
                    options,
                    AudienceOptions(),
                    new FeedTelemetry(),
                    new FixedTimeProvider(Now.AddSeconds(5)));
                Assert.Equal(500, await revocations.ProcessBatchAsync("privacy-a", Now.AddSeconds(5), cancellationToken));
                Assert.Equal(500, await revocations.ProcessBatchAsync("privacy-a", Now.AddSeconds(6), cancellationToken));
                Assert.Equal(201, await revocations.ProcessBatchAsync("privacy-a", Now.AddSeconds(7), cancellationToken));
                Assert.Equal(
                    1_201,
                    await context.FeedEntries.CountAsync(
                        entry => entry.RevokedAtUtc != null,
                        cancellationToken));
                Assert.Equal(
                    FeedWorkStates.Completed,
                    (await context.FeedRevocationWorkItems.SingleAsync(cancellationToken)).State);

                // The production worker uses a fresh scope per cycle. Clear tracked entities
                // here as the test intentionally drives several otherwise-separate cycles.
                context.ChangeTracker.Clear();
                Assert.Equal(500, await processor.ProcessBatchAsync("worker-b", Now.AddSeconds(9), cancellationToken));
                Assert.Equal(500, await processor.ProcessBatchAsync("worker-b", Now.AddSeconds(10), cancellationToken));
                Assert.Equal(201, await processor.ProcessBatchAsync("worker-b", Now.AddSeconds(11), cancellationToken));
                Assert.Equal(2, await context.FanoutWorkItems.CountAsync(cancellationToken));
                Assert.Equal(1_201, await context.FeedEntries.CountAsync(cancellationToken));
                Assert.Equal(
                    0,
                    await context.FeedEntries.CountAsync(
                        entry => entry.RevokedAtUtc != null,
                        cancellationToken));
            }
        });

    [Fact]
    [Trait("Category", "Integration")]
    public Task Hybrid_reader_caps_pull_publishers_at_64_and_paginates_a_stable_deduplicated_tuple() =>
        InIsolatedDatabase("feed_reader", async (connectionString, cancellationToken) =>
        {
            var dbOptions = ContextOptions(connectionString);
            await using var context = new FeedContext(dbOptions);
            await context.Database.MigrateAsync(cancellationToken);
            await context.FeedAudienceBootstrapStates.AddAsync(
                CompleteBootstrap(Now),
                cancellationToken);
            const int viewerUserId = 900;
            for (var publisher = 1; publisher <= 65; publisher++)
            {
                await AddActiveFollowAsync(context, publisher, viewerUserId, cancellationToken);
                await context.FeedPublisherStates.AddAsync(
                    FeedPublisherState.Create(
                        publisher,
                        100_000,
                        100_000,
                        80_000,
                        Now),
                    cancellationToken);
                await context.FeedActivities.AddAsync(
                    FeedActivity.Create(
                        "post",
                        $"pull-{publisher}",
                        "created",
                        publisher,
                        FeedAudiencePolicies.Followers,
                        Now.AddSeconds(-publisher),
                        Now,
                        sourceStreamId: Guid.NewGuid(),
                        eventId: Guid.NewGuid(),
                        rankKey: 10_000 - publisher,
                        deliveryMode: FeedDeliveryModes.Pull),
                    cancellationToken);
            }
            await context.SaveChangesAsync(cancellationToken);

            var options = TestOptions();
            var reader = new FeedReader(
                context,
                new FeedUserErasureAuthority(context),
                new NeverCache(),
                new NeverCacheKeys(),
                Options.Create(options),
                AudienceOptions(),
                CursorOptions(),
                new FeedTelemetry(),
                new FixedTimeProvider(Now.AddHours(1)));
            var first = await reader.ReadAsync(
                viewerUserId,
                new FeedPageRequest(null, 50, true),
                cancellationToken);
            var second = await reader.ReadAsync(
                viewerUserId,
                new FeedPageRequest(first.NextCursor, 50, true),
                cancellationToken);

            Assert.Equal(50, first.Items.Count);
            Assert.Equal(14, second.Items.Count);
            Assert.True(first.ContainsPullItems);
            Assert.NotNull(first.NextCursor);
            Assert.False(second.HasMore);
            Assert.Equal(
                64,
                first.Items.Concat(second.Items).Select(item => item.ActivityId).Distinct().Count());
            Assert.DoesNotContain(
                first.Items.Concat(second.Items),
                item => item.ActorUserId == 65);

            var acknowledgedActivityId = first.Items[0].ActivityId;
            Assert.Equal(
                1,
                await reader.AcknowledgeAsync(
                    viewerUserId,
                    [acknowledgedActivityId],
                    Now.AddHours(1),
                    cancellationToken));
            Assert.Equal(
                0,
                await reader.AcknowledgeAsync(
                    viewerUserId,
                    [acknowledgedActivityId],
                    Now.AddHours(1).AddSeconds(1),
                    cancellationToken));
            var unseen = await reader.ReadAsync(
                viewerUserId,
                new FeedPageRequest(null, 50, false),
                cancellationToken);
            Assert.DoesNotContain(unseen.Items, item => item.ActivityId == acknowledgedActivityId);
            var includingSeen = await reader.ReadAsync(
                viewerUserId,
                new FeedPageRequest(null, 50, true),
                cancellationToken);
            var seenItem = Assert.Single(
                includingSeen.Items,
                item => item.ActivityId == acknowledgedActivityId);
            Assert.NotNull(seenItem.SeenAtUtc);
            Assert.True(seenItem.EntryId > 0);

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(
                """
                EXPLAIN SELECT `Id` FROM `FeedActivity`
                WHERE `ActorUserId` = 1 AND `IsTombstoned` = 0 AND `DeliveryMode` = 'pull'
                ORDER BY `RankKey` DESC, `PublishedAtUtc` DESC, `Id` DESC LIMIT 50
                """,
                connection);
            await using var result = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await result.ReadAsync(cancellationToken));
            var chosenKey = result.IsDBNull(result.GetOrdinal("key"))
                ? string.Empty
                : result.GetString(result.GetOrdinal("key"));
            Assert.Contains("IX_FeedActivity_Pull_Cursor", chosenKey, StringComparison.Ordinal);
        });

    [Fact]
    [Trait("Category", "Integration")]
    public Task Maintenance_compacts_in_bounded_pages_and_replay_is_idempotent() =>
        InIsolatedDatabase("feed_maintenance", async (connectionString, cancellationToken) =>
        {
            var dbOptions = ContextOptions(connectionString);
            await using var context = new FeedContext(dbOptions);
            await context.Database.MigrateAsync(cancellationToken);
            var old = Now.AddDays(-60);
            var expired = FeedActivity.Create(
                "post",
                "expired",
                "created",
                10,
                FeedAudiencePolicies.Followers,
                old,
                old,
                sourceStreamId: Guid.NewGuid(),
                eventId: Guid.NewGuid());
            expired.Tombstone(old.AddMinutes(1));
            var expiredEntry = FeedEntry.Create(expired, 20, FeedDeliveryModes.Push, old.Ticks, old);
            var expiredWork = FanoutWorkItem.Create("expired-work", expired, old);
            var lease = expiredWork.Lease("maintenance-seed", old, TimeSpan.FromSeconds(10));
            expiredWork.InitializeAudienceSnapshot(lease, 0, old.AddSeconds(1));
            expiredWork.Advance(lease, 0, completed: true, old.AddSeconds(2));
            var replayable = FeedActivity.Create(
                "post",
                "replayable",
                "created",
                11,
                FeedAudiencePolicies.Followers,
                Now,
                Now,
                sourceStreamId: Guid.NewGuid(),
                eventId: Guid.NewGuid());
            await context.AddRangeAsync(expired, expiredEntry, expiredWork, replayable);
            await context.SaveChangesAsync(cancellationToken);

            var maintenance = new FeedMaintenanceProcessor(
                context,
                new FeedUserErasureAuthority(context),
                Options.Create(TestOptions()),
                new FeedTelemetry());
            Assert.Equal(3, await maintenance.CompactAsync(Now, cancellationToken));
            context.ChangeTracker.Clear();
            Assert.DoesNotContain(
                await context.FeedActivities.Select(activity => activity.TargetId).ToListAsync(cancellationToken),
                target => target == "expired");
            Assert.Equal(1, await maintenance.ReplayAsync(Now, cancellationToken));
            Assert.Equal(0, await maintenance.ReplayAsync(Now.AddSeconds(1), cancellationToken));
            Assert.Single(await context.FanoutWorkItems.ToListAsync(cancellationToken));
            Assert.Contains(
                await context.FeedMaintenanceCheckpoints.Select(item => item.JobName).ToListAsync(cancellationToken),
                name => name == "replay-rebuild");
        });

    private static FeedOptions TestOptions() => new()
    {
        EnableFirstPageCache = false,
        PullEnterAudienceCount = 100_000,
        PullExitAudienceCount = 80_000,
        MaximumPullPublishers = 64,
        MaximumCandidates = 512,
        MaximumPageSize = 50,
        FanoutBatchSize = 500,
        FanoutClaimBatchSize = 16,
        RevocationBatchSize = 500,
        LeaseSeconds = 30,
        MaximumAttempts = 20,
        MaximumCheckpointAgeSeconds = 3_600,
    };

    private static IOptions<AudienceProjectionOptions> AudienceOptions() =>
        Options.Create(new AudienceProjectionOptions
        {
            MaximumCheckpointAgeSeconds = 3_600,
            MaximumBootstrapAgeSeconds = 3_600,
        });

    private static IOptions<FeedCursorOptions> CursorOptions() =>
        Options.Create(new FeedCursorOptions
        {
            ActiveKeyId = "test",
            SigningKeys = new Dictionary<string, string>
            {
                ["test"] = Convert.ToBase64String(
                    Enumerable.Repeat((byte)0x51, 32).ToArray()),
            },
        });

    private static FeedAudienceBootstrapState CompleteBootstrap(DateTime observedAtUtc)
    {
        var state = FeedAudienceBootstrapState.Create(observedAtUtc);
        var lease = state.TryLease("integration-bootstrap", observedAtUtc, TimeSpan.FromMinutes(1));
        Assert.NotNull(lease);
        state.BeginCycle(lease.Value, observedAtUtc);
        state.ApplyPage(
            lease.Value,
            0,
            observedAtUtc,
            0,
            hasMore: false,
            observedAtUtc: observedAtUtc);
        return state;
    }

    private static async Task AddActiveFollowAsync(
        FeedContext context,
        int ownerUserId,
        int viewerUserId,
        CancellationToken cancellationToken)
    {
        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var checkpoint = FeedRelationshipCheckpoint.Create(
            streamId,
            ownerUserId,
            viewerUserId,
            RelationshipKinds.Follow,
            Now);
        checkpoint.MarkApplied(eventId, 1, Now, Now);
        await context.FeedRelationshipCheckpoints.AddAsync(checkpoint, cancellationToken);
        await context.FeedAudienceEdges.AddAsync(
            FeedAudienceEdge.Create(
                ownerUserId,
                viewerUserId,
                RelationshipKinds.Follow,
                RelationshipStates.Active,
                streamId,
                eventId,
                1,
                Now,
                Now),
            cancellationToken);
    }

    private static DbContextOptions<FeedContext> ContextOptions(string connectionString) =>
        new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                connectionString,
                ServerVersion,
                provider => provider.CommandTimeout(30))
            .Options;

    private static async Task InIsolatedDatabase(
        string prefix,
        Func<string, CancellationToken, Task> test)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");
        var databaseName = $"{prefix}_{Guid.NewGuid():N}";
        var connection = WithDatabase(configured!, databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await ExecuteAdministrativeCommand(
                configured!,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                timeout.Token);
            await test(connection, timeout.Token);
        }
        finally
        {
            await ExecuteAdministrativeCommand(
                configured!,
                $"DROP DATABASE IF EXISTS `{databaseName}`;",
                CancellationToken.None);
        }
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { Database = databaseName };
        return builder.ConnectionString;
    }

    private static async Task ExecuteAdministrativeCommand(
        string connectionString,
        string commandText,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class NeverCache : ISharedCache
    {
        public ValueTask<T> GetOrCreateAsync<T>(
            CacheKey key,
            Func<CancellationToken, ValueTask<T>> authoritativeSource,
            CacheEntryPolicy? policy = null,
            CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Pull pages must bypass the first-page cache.");

        public ValueTask RemoveAsync(CacheKey key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class NeverCacheKeys : ICacheKeyFactory
    {
        public CacheKey Create(
            string resource,
            string entityId,
            long entityVersion,
            string? tenantId = null) =>
            throw new InvalidOperationException("Pull pages must not create a cache key.");
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
