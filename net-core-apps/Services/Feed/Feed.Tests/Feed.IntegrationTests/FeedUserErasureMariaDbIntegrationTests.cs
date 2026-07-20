using Feed.Application.Features.Privacy;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedUserErasureMariaDbIntegrationTests
{
    private static readonly DateTime Now =
        new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tombstone_is_version_aware_and_blocks_reads_live_events_and_bootstrap_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await OpenDatabaseAsync(cancellationToken);
        if (database is null)
            return;
        await using var context = database.Context;
        try
        {
            await SeedPublisherDataAsync(context, userId: 42, recipients: [42], cancellationToken);
            await using var publisher = RecordingPublisher.Create(context);
            var authority = new FeedUserErasureAuthority(context);
            var scheduler = new FeedUserErasureScheduler(
                context,
                authority,
                publisher,
                new FeedUserErasureMetrics());
            var requestId = Guid.NewGuid();
            var original = Request(requestId, userId: 42, version: 2);

            var scheduled = await scheduler.ScheduleAsync(original, Now, cancellationToken);
            Assert.True(scheduled.Scheduled);
            Assert.True(await authority.IsErasedAsync(42, cancellationToken));

            var duplicate = await scheduler.ScheduleAsync(original, Now, cancellationToken);
            Assert.True(duplicate.Duplicate);
            Assert.Empty(publisher.Messages);

            var lower = await scheduler.ScheduleAsync(
                Request(Guid.NewGuid(), 42, 1), Now, cancellationToken);
            Assert.True(lower.Superseded);
            Assert.Contains(publisher.Messages, message =>
                message.Outcome == UserErasureOutcomes.Superseded
                && message.ErasureVersion == 1);

            var conflicting = await scheduler.ScheduleAsync(
                Request(Guid.NewGuid(), 42, 2), Now, cancellationToken);
            Assert.True(conflicting.Superseded);

            var newerRequestId = Guid.NewGuid();
            var newer = await scheduler.ScheduleAsync(
                Request(newerRequestId, 42, 3), Now, cancellationToken);
            Assert.True(newer.Scheduled);
            var tombstone = await context.FeedUserErasures.AsNoTracking()
                .SingleAsync(item => item.UserId == 42, cancellationToken);
            Assert.Equal(3, tombstone.ErasureVersion);
            Assert.Equal(newerRequestId, tombstone.RequestId);

            var activityStore = new FeedActivityProjectionStore(
                context,
                authority,
                Options.Create(new FeedOptions()),
                new FeedTelemetry(),
                new MutableTimeProvider(Now));
            await activityStore.ApplyCanonicalAsync(
                ActivityEvent(actorUserId: 42, targetId: "late-activity"),
                Now,
                cancellationToken);
            Assert.False(await context.FeedActivities.AsNoTracking()
                .AnyAsync(item => item.TargetId == "late-activity", cancellationToken));

            var audienceStore = new AudienceProjectionStore(
                context,
                authority,
                new AudienceProjectionTelemetry(),
                Options.Create(new AudienceProjectionOptions()),
                Options.Create(new FeedOptions()),
                new MutableTimeProvider(Now));
            await audienceStore.ApplyAsync(
                RelationshipEvent(owner: 42, viewer: 77),
                Now,
                cancellationToken);
            Assert.False(await context.FeedAudienceEdges.AsNoTracking()
                .AnyAsync(item => item.OwnerUserId == 42 && item.ViewerUserId == 77, cancellationToken));
            Assert.Equal(0, await audienceStore.ReconcilePageAsync(
                [RelationshipSnapshot(owner: 88, viewer: 42)],
                Now,
                cancellationToken));

            await context.FanoutWorkItems
                .Where(item => item.PublisherUserId == 42)
                .ExecuteDeleteAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var maintenance = new FeedMaintenanceProcessor(
                context,
                authority,
                Options.Create(new FeedOptions { FanoutBatchSize = 20 }),
                new FeedTelemetry());
            await maintenance.ReplayAsync(Now, cancellationToken);
            Assert.False(await context.FanoutWorkItems.AsNoTracking()
                .AnyAsync(item => item.PublisherUserId == 42, cancellationToken));
        }
        finally
        {
            await database.DeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Worker_is_bounded_erases_every_role_bumps_revisions_and_acks_after_cache_expiry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await OpenDatabaseAsync(cancellationToken);
        if (database is null)
            return;
        await using var context = database.Context;
        try
        {
            await SeedPublisherDataAsync(
                context,
                userId: 42,
                recipients: [101, 102, 103, 104, 105, 106, 107],
                cancellationToken);
            await SeedReaderAndRelationshipDataAsync(context, 42, cancellationToken);

            var clock = new MutableTimeProvider(Now);
            await using var publisher = RecordingPublisher.Create(context);
            var authority = new FeedUserErasureAuthority(context);
            var scheduler = new FeedUserErasureScheduler(
                context,
                authority,
                publisher,
                new FeedUserErasureMetrics());
            await scheduler.ScheduleAsync(
                Request(Guid.NewGuid(), 42, 1),
                clock.GetUtcNow().UtcDateTime,
                cancellationToken);
            var erasureOptions = Options.Create(new FeedUserErasureOptions
            {
                BatchSize = 2,
                MaximumRowsPerCycle = 4,
                MaximumBatchesPerCycle = 4,
                CycleTimeBudgetMilliseconds = 1_000,
                LeaseSeconds = 10,
                CacheExpirySafetySeconds = 0,
            });
            var processor = new FeedUserErasureProcessor(
                context,
                authority,
                publisher,
                clock,
                erasureOptions,
                Options.Create(new FeedOptions { FirstPageCacheSeconds = 1 }),
                new FeedUserErasureMetrics());

            for (var cycle = 0; cycle < 200; cycle++)
            {
                var result = await processor.ProcessCycleAsync("feed-test-worker", cancellationToken);
                Assert.True(result.Rows <= erasureOptions.Value.MaximumRowsPerCycle);
                context.ChangeTracker.Clear();
                var progress = await context.FeedUserErasures.AsNoTracking()
                    .SingleAsync(item => item.UserId == 42, cancellationToken);
                if (progress.Stage == FeedUserErasureStage.CacheExpiryBarrier
                    && progress.CacheSafeAfterUtc.HasValue)
                    break;
            }

            var barrier = await context.FeedUserErasures.AsNoTracking()
                .SingleAsync(item => item.UserId == 42, cancellationToken);
            Assert.Equal(FeedUserErasureStage.CacheExpiryBarrier, barrier.Stage);
            Assert.Equal(FeedUserErasureStatus.Pending, barrier.Status);
            Assert.DoesNotContain(publisher.Messages, message =>
                message.Outcome == UserErasureOutcomes.Completed);
            await AssertNoDurableReferencesAsync(context, 42, cancellationToken);

            clock.Advance(TimeSpan.FromSeconds(2));
            for (var cycle = 0; cycle < 10; cycle++)
            {
                var result = await processor.ProcessCycleAsync("feed-test-worker", cancellationToken);
                if (result.Completed)
                    break;
            }

            context.ChangeTracker.Clear();
            var completed = await context.FeedUserErasures.AsNoTracking()
                .SingleAsync(item => item.UserId == 42, cancellationToken);
            Assert.Equal(FeedUserErasureStatus.Completed, completed.Status);
            Assert.NotNull(completed.CompletionEventId);
            Assert.Single(publisher.Messages, message =>
                message.Outcome == UserErasureOutcomes.Completed
                && message.RequestId == completed.RequestId);
            Assert.All(
                await context.FeedUserStates.AsNoTracking()
                    .Where(item => item.UserId >= 101 && item.UserId <= 107)
                    .ToListAsync(cancellationToken),
                state => Assert.True(state.FeedRevision > 0));
            await AssertNoDurableReferencesAsync(context, 42, cancellationToken);
        }
        finally
        {
            await database.DeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task MariaDb_fences_stale_lease_and_failed_completion_replays_without_premature_ack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await OpenDatabaseAsync(cancellationToken);
        if (database is null)
            return;
        try
        {
            var publisherState = new RecordingPublisherState();
            await using (var setup = database.Context)
            {
                await using var setupPublisher =
                    RecordingPublisher.Create(setup, publisherState);
                var authority = new FeedUserErasureAuthority(setup);
                var scheduler = new FeedUserErasureScheduler(
                    setup,
                    authority,
                    setupPublisher,
                    new FeedUserErasureMetrics());
                await scheduler.ScheduleAsync(
                    Request(Guid.NewGuid(), 55, 1), Now, cancellationToken);
            }

            await using var first = database.CreateContext();
            await using var second = database.CreateContext();
            var firstJob = await first.FeedUserErasures.SingleAsync(
                item => item.UserId == 55, cancellationToken);
            firstJob.Claim("worker-a", Now.AddSeconds(10), Now);
            await first.SaveChangesAsync(cancellationToken);
            var firstToken = firstJob.LeaseToken;

            var secondJob = await second.FeedUserErasures.SingleAsync(
                item => item.UserId == 55, cancellationToken);
            secondJob.Claim("worker-b", Now.AddSeconds(40), Now.AddSeconds(20));
            await second.SaveChangesAsync(cancellationToken);

            firstJob.Advance(
                FeedUserErasureStage.PublisherFanoutWork,
                1,
                0,
                "worker-a",
                firstToken,
                Now.AddSeconds(9),
                Now.AddSeconds(1));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                first.SaveChangesAsync(cancellationToken));

            await using var replayContext = database.CreateContext();
            await using var replayPublisher =
                RecordingPublisher.Create(replayContext, publisherState);
            var replayClock = new MutableTimeProvider(Now.AddMinutes(1));
            var replayAuthority = new FeedUserErasureAuthority(replayContext);
            var processor = new FeedUserErasureProcessor(
                replayContext,
                replayAuthority,
                replayPublisher,
                replayClock,
                Options.Create(new FeedUserErasureOptions
                {
                    BatchSize = 20,
                    MaximumRowsPerCycle = 400,
                    MaximumBatchesPerCycle = 32,
                    CycleTimeBudgetMilliseconds = 1_000,
                    LeaseSeconds = 10,
                    CacheExpirySafetySeconds = 0,
                }),
                Options.Create(new FeedOptions { FirstPageCacheSeconds = 1 }),
                new FeedUserErasureMetrics());
            await processor.ProcessCycleAsync("replay-worker", cancellationToken);
            replayClock.Advance(TimeSpan.FromSeconds(2));
            publisherState.FailCompleted = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessCycleAsync("replay-worker", cancellationToken));
            replayContext.ChangeTracker.Clear();
            Assert.NotEqual(
                FeedUserErasureStatus.Completed,
                (await replayContext.FeedUserErasures.AsNoTracking()
                    .SingleAsync(item => item.UserId == 55, cancellationToken)).Status);
            Assert.DoesNotContain(publisherState.Messages, message =>
                message.Outcome == UserErasureOutcomes.Completed);

            publisherState.FailCompleted = false;
            replayClock.Advance(TimeSpan.FromSeconds(11));
            var replay = await processor.ProcessCycleAsync("replay-worker", cancellationToken);
            Assert.True(replay.Completed);
            Assert.Single(publisherState.Messages, message =>
                message.Outcome == UserErasureOutcomes.Completed);
        }
        finally
        {
            await database.DeleteAsync(cancellationToken);
        }
    }

    private static async Task SeedPublisherDataAsync(
        FeedContext context,
        int userId,
        IReadOnlyCollection<int> recipients,
        CancellationToken cancellationToken)
    {
        var activity = FeedActivity.Create(
            "post",
            $"publisher-{userId}",
            "created",
            userId,
            FeedAudiencePolicies.Public,
            Now,
            Now,
            Guid.NewGuid(),
            Guid.NewGuid());
        context.FeedActivities.Add(activity);
        await context.SaveChangesAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            context.FeedEntries.Add(FeedEntry.Create(
                activity,
                recipient,
                FeedDeliveryModes.Push,
                activity.RankKey,
                Now));
        }
        context.FanoutWorkItems.Add(FanoutWorkItem.Create(
            $"privacy-fanout-{userId}", activity, Now));
        context.FeedRevocationWorkItems.Add(FeedRevocationWorkItem.ForActivity(
            $"privacy-revoke-{userId}", activity, Now, Now));
        context.FeedPublisherStates.Add(FeedPublisherState.Create(
            userId, recipients.Count, 100_000, 80_000, Now));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedReaderAndRelationshipDataAsync(
        FeedContext context,
        int userId,
        CancellationToken cancellationToken)
    {
        var otherActivity = FeedActivity.Create(
            "post", "other-publisher", "created", 99,
            FeedAudiencePolicies.Public, Now, Now,
            Guid.NewGuid(), Guid.NewGuid());
        context.FeedActivities.Add(otherActivity);
        await context.SaveChangesAsync(cancellationToken);
        context.FeedEntries.Add(FeedEntry.Create(
            otherActivity, userId, FeedDeliveryModes.Push, otherActivity.RankKey, Now));
        context.FeedUserStates.Add(FeedUserState.Create(userId, Now));
        context.FeedRevocationWorkItems.Add(FeedRevocationWorkItem.ForRelationship(
            "privacy-viewer-revoke", 99, userId, FeedAudiencePolicies.Public, false, Now, Now));
        AddRelationship(context, userId, 101);
        AddRelationship(context, 99, userId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void AddRelationship(FeedContext context, int owner, int viewer)
    {
        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var checkpoint = FeedRelationshipCheckpoint.Create(
            streamId, owner, viewer, RelationshipKinds.Follow, Now);
        checkpoint.Reconcile(eventId, 1, Now, Now);
        context.FeedRelationshipCheckpoints.Add(checkpoint);
        context.FeedAudienceEdges.Add(FeedAudienceEdge.Create(
            owner,
            viewer,
            RelationshipKinds.Follow,
            RelationshipStates.Active,
            streamId,
            eventId,
            1,
            Now,
            Now));
    }

    private static async Task AssertNoDurableReferencesAsync(
        FeedContext context,
        int userId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        Assert.False(await context.FeedActivities.AsNoTracking()
            .AnyAsync(item => item.ActorUserId == userId, cancellationToken));
        Assert.False(await context.FeedEntries.AsNoTracking()
            .AnyAsync(item => item.RecipientUserId == userId
                || item.Activity.ActorUserId == userId, cancellationToken));
        Assert.False(await context.FanoutWorkItems.AsNoTracking()
            .AnyAsync(item => item.PublisherUserId == userId, cancellationToken));
        Assert.False(await context.FeedRevocationWorkItems.AsNoTracking()
            .AnyAsync(item => item.OwnerUserId == userId || item.ViewerUserId == userId, cancellationToken));
        Assert.False(await context.FeedAudienceEdges.AsNoTracking()
            .AnyAsync(item => item.OwnerUserId == userId || item.ViewerUserId == userId, cancellationToken));
        Assert.False(await context.FeedRelationshipCheckpoints.AsNoTracking()
            .AnyAsync(item => item.OwnerUserId == userId || item.ViewerUserId == userId, cancellationToken));
        Assert.False(await context.FeedPublisherStates.AsNoTracking()
            .AnyAsync(item => item.PublisherUserId == userId, cancellationToken));
        Assert.False(await context.FeedUserStates.AsNoTracking()
            .AnyAsync(item => item.UserId == userId, cancellationToken));
        Assert.True(await context.FeedUserErasures.AsNoTracking()
            .AnyAsync(item => item.UserId == userId, cancellationToken));
    }

    private static UserErasureRequested Request(Guid requestId, int userId, long version) => new()
    {
        EventId = Guid.NewGuid(),
        RequestId = requestId,
        UserId = userId,
        ErasureVersion = version,
        RequestedAtUtc = new DateTimeOffset(Now),
    };

    private static FeedActivityChanged ActivityEvent(int actorUserId, string targetId) => new()
    {
        EventId = Guid.NewGuid(),
        StreamId = Guid.NewGuid(),
        StreamVersion = 1,
        TargetType = "post",
        TargetId = targetId,
        ActivityKind = "created",
        ActorUserId = actorUserId,
        AudiencePolicy = FeedAudiencePolicies.Public,
        State = FeedActivityLifecycle.Active,
        RankKey = Now.Ticks,
        PublishedAtUtc = Now,
        OccurredAtUtc = Now,
        ReasonCode = "privacy-test",
    };

    private static RelationshipStateChanged RelationshipEvent(int owner, int viewer) => new()
    {
        EventId = Guid.NewGuid(),
        StreamId = Guid.NewGuid(),
        StreamVersion = 1,
        OwnerUserId = owner,
        ViewerUserId = viewer,
        RelationshipKind = RelationshipKinds.Follow,
        State = RelationshipStates.Active,
        OccurredAtUtc = Now,
        ReasonCode = "privacy-test",
    };

    private static RelationshipStateSnapshot RelationshipSnapshot(int owner, int viewer) => new()
    {
        Cursor = 1,
        StreamId = Guid.NewGuid(),
        LastEventId = Guid.NewGuid(),
        StreamVersion = 1,
        OwnerUserId = owner,
        ViewerUserId = viewer,
        RelationshipKind = RelationshipKinds.Follow,
        State = RelationshipStates.Active,
        AsOfUtc = Now,
    };

    private static async Task<TestDatabase?> OpenDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run Feed privacy MariaDB integration tests.");
            return null;
        }
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"feed_privacy_{Guid.NewGuid():N}",
        };
        var database = new TestDatabase(builder.ConnectionString);
        await database.Context.Database.MigrateAsync(cancellationToken);
        return database;
    }

    private sealed class TestDatabase(string connectionString)
    {
        public FeedContext Context { get; } = Create(connectionString);
        public FeedContext CreateContext() => Create(connectionString);

        public async Task DeleteAsync(CancellationToken cancellationToken)
        {
            await using var cleanup = CreateContext();
            await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }

        private static FeedContext Create(string value) => new(
            new DbContextOptionsBuilder<FeedContext>()
                .UseMySql(value, new MariaDbServerVersion(new Version(10, 6, 27)))
                .Options);
    }

    private sealed class RecordingPublisher
        : IIntegrationMessagePublisher, IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ISendEndpointProvider _endpoints;
        private readonly RecordingPublisherState _state;

        private RecordingPublisher(
            FeedContext context,
            RecordingPublisherState state)
        {
            _state = state;
            var services = new ServiceCollection();
            services.AddScoped(_ => context);
            services.AddMassTransit(registration =>
            {
                registration.AddEntityFrameworkOutbox<FeedContext>(outbox =>
                {
                    outbox.UseMySql();
                    outbox.UseBusOutbox();
                });
                registration.UsingInMemory((_, _) => { });
            });
            _services = services.BuildServiceProvider();
            _endpoints = _services.GetRequiredService<ISendEndpointProvider>();
        }

        public IReadOnlyList<UserErasureCompleted> Messages => _state.Messages;
        public bool FailCompleted
        {
            get => _state.FailCompleted;
            set => _state.FailCompleted = value;
        }

        public static RecordingPublisher Create(
            FeedContext context,
            RecordingPublisherState? state = null) =>
            new(context, state ?? new RecordingPublisherState());

        public async Task<Guid> PublishAsync<TMessage>(
            TMessage message,
            Guid messageId,
            CancellationToken cancellationToken)
            where TMessage : class
        {
            if (message is UserErasureCompleted completion)
            {
                if (FailCompleted && completion.Outcome == UserErasureOutcomes.Completed)
                    throw new InvalidOperationException("Injected completion outbox failure.");
            }

            var endpoint = await _endpoints.GetSendEndpoint(
                new Uri("queue:feed-privacy-integration-probe"));
            await endpoint.Send(
                message,
                pipe => pipe.MessageId = messageId,
                cancellationToken);
            if (message is UserErasureCompleted staged)
                _state.Messages.Add(staged);
            return messageId;
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    private sealed class RecordingPublisherState
    {
        public List<UserErasureCompleted> Messages { get; } = [];
        public bool FailCompleted { get; set; }
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = new(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
