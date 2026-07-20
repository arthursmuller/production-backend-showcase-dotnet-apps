using Feed.Application.Features.Audience;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using MessageBrokerContracts.Feed;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedAudienceAuthorizationIntegrationTests
{
    private static readonly DateTime Now =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MariaDb_authorization_is_complete_bounded_and_revokes_broken_fof_paths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run Feed audience authorization integration tests.");

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"feed_audience_auth_{Guid.NewGuid():N}",
        };
        var dbOptions = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                builder.ConnectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        var audienceOptions = Options.Create(new AudienceProjectionOptions
        {
            MaximumBootstrapPageSize = 500,
            MaximumCheckpointAgeSeconds = 3600,
            MaximumBootstrapAgeSeconds = 3600,
            MaximumFriendsOfFriendsCandidates = 256,
            AuthorizationDeadlineMilliseconds = 1000,
        });
        var feedOptions = Options.Create(new FeedOptions
        {
            MaximumCheckpointAgeSeconds = 3600,
        });

        await using var context = new FeedContext(dbOptions);
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            var telemetry = new AudienceProjectionTelemetry();
            var erasureAuthority = new FeedUserErasureAuthority(context);
            var store = new AudienceProjectionStore(
                context,
                erasureAuthority,
                telemetry,
                audienceOptions,
                feedOptions,
                new FixedTimeProvider(Now.AddMinutes(10)));

            Assert.True((await store.EvaluateAsync(
                10, 10, FeedAudiencePolicies.Owner, Now,
                TimeSpan.FromHours(1), cancellationToken)).Allowed);
            var missing = await store.EvaluateAsync(
                10, 20, FeedAudiencePolicies.Public, Now,
                TimeSpan.FromHours(1), cancellationToken);
            Assert.False(missing.Allowed);
            Assert.Equal("bootstrap_missing", missing.ReasonCode);

            var bootstrap = CompleteBootstrap(Now);
            context.FeedAudienceBootstrapStates.Add(bootstrap);
            AddRelationship(context, 10, 20, RelationshipKinds.Follow, RelationshipStates.Active);
            AddRelationship(context, 10, 20, RelationshipKinds.Friend, RelationshipStates.Active);
            AddRelationship(context, 30, 40, RelationshipKinds.Friend, RelationshipStates.Active);
            AddRelationship(context, 40, 20, RelationshipKinds.Friend, RelationshipStates.Active);
            await context.SaveChangesAsync(cancellationToken);

            var healthCheck = new AudienceProjectionHealthCheck(
                context,
                audienceOptions,
                new FixedTimeProvider(Now),
                telemetry);
            Assert.Equal(
                HealthStatus.Healthy,
                (await healthCheck.CheckHealthAsync(
                    new HealthCheckContext(),
                    cancellationToken)).Status);

            Assert.True((await Evaluate(store, 10, 20, FeedAudiencePolicies.Public, cancellationToken)).Allowed);
            Assert.True((await Evaluate(store, 10, 20, FeedAudiencePolicies.Followers, cancellationToken)).Allowed);
            Assert.True((await Evaluate(store, 10, 20, FeedAudiencePolicies.Friends, cancellationToken)).Allowed);
            Assert.True((await Evaluate(store, 10, 20, FeedAudiencePolicies.FriendsOfFriends, cancellationToken)).Allowed);
            Assert.True((await Evaluate(store, 30, 20, FeedAudiencePolicies.FriendsOfFriends, cancellationToken)).Allowed);
            Assert.False((await Evaluate(store, 10, 20, FeedAudiencePolicies.Owner, cancellationToken)).Allowed);
            Assert.False((await Evaluate(store, 10, 20, "future-policy", cancellationToken)).Allowed);

            AddRelationship(
                context,
                200,
                201,
                RelationshipKinds.Friend,
                RelationshipStates.Active,
                Now.AddHours(-2));
            await context.SaveChangesAsync(cancellationToken);
            var staleLiveCheckpoint = await Evaluate(
                store,
                200,
                201,
                FeedAudiencePolicies.Friends,
                cancellationToken);
            Assert.False(staleLiveCheckpoint.Allowed);
            Assert.Equal(
                "stale_or_unhealthy_friend_checkpoint",
                staleLiveCheckpoint.ReasonCode);
            var staleProjectionHealth = await healthCheck.CheckHealthAsync(
                new HealthCheckContext(),
                cancellationToken);
            Assert.Equal(HealthStatus.Unhealthy, staleProjectionHealth.Status);
            Assert.Contains("stale", staleProjectionHealth.Description, StringComparison.OrdinalIgnoreCase);

            AddRelationship(context, 40, 30, RelationshipKinds.Block, RelationshipStates.Blocked);
            await context.SaveChangesAsync(cancellationToken);
            Assert.False((await Evaluate(
                store, 30, 20, FeedAudiencePolicies.FriendsOfFriends, cancellationToken)).Allowed);
            AddRelationship(context, 20, 30, RelationshipKinds.Block, RelationshipStates.Blocked);
            await context.SaveChangesAsync(cancellationToken);
            Assert.False((await Evaluate(
                store, 30, 20, FeedAudiencePolicies.FriendsOfFriends, cancellationToken)).Allowed);
            var stale = await store.EvaluateAsync(
                10, 20, FeedAudiencePolicies.Public, Now.AddHours(2),
                TimeSpan.FromHours(24), cancellationToken);
            Assert.False(stale.Allowed);
            Assert.Equal("bootstrap_stale", stale.ReasonCode);

            var gapStream = Guid.NewGuid();
            var gap = await store.ApplyAsync(
                RelationshipEvent(
                    gapStream,
                    Guid.NewGuid(),
                    version: 3,
                    owner: 50,
                    viewer: 60,
                    RelationshipKinds.Friend,
                    RelationshipStates.Active),
                Now.AddMinutes(1),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.GapQuarantined, gap.Outcome);
            Assert.False((await Evaluate(
                store, 10, 20, FeedAudiencePolicies.Public, cancellationToken)).Allowed);
            var gappedProjectionHealth = await healthCheck.CheckHealthAsync(
                new HealthCheckContext(),
                cancellationToken);
            Assert.Equal(HealthStatus.Unhealthy, gappedProjectionHealth.Status);
            Assert.Contains("gap", gappedProjectionHealth.Description, StringComparison.OrdinalIgnoreCase);

            await store.ReconcilePageAsync(
                [new RelationshipStateSnapshot
                {
                    Cursor = 1,
                    StreamId = gapStream,
                    LastEventId = Guid.NewGuid(),
                    StreamVersion = 3,
                    OwnerUserId = 50,
                    ViewerUserId = 60,
                    RelationshipKind = RelationshipKinds.Friend,
                    State = RelationshipStates.Active,
                    AsOfUtc = Now.AddMinutes(2),
                }],
                Now.AddMinutes(2),
                cancellationToken);
            CompleteExistingBootstrap(bootstrap, Now.AddMinutes(2));
            await context.SaveChangesAsync(cancellationToken);

            for (var intermediate = 1000; intermediate <= 1256; intermediate++)
            {
                AddRelationship(
                    context,
                    100,
                    intermediate,
                    RelationshipKinds.Friend,
                    RelationshipStates.Active,
                    Now.AddMinutes(2));
            }
            await context.SaveChangesAsync(cancellationToken);
            var capped = await store.EvaluateAsync(
                100, 999, FeedAudiencePolicies.FriendsOfFriends, Now.AddMinutes(2),
                TimeSpan.FromHours(1), cancellationToken);
            Assert.False(capped.Allowed);
            Assert.Equal("friends_of_friends_candidate_budget", capped.ReasonCode);

            var firstLegStream = Guid.NewGuid();
            var activeEventId = Guid.NewGuid();
            var revocationEventId = Guid.NewGuid();
            var revocationSourceOccurredAtUtc = Now.AddMinutes(2);
            await store.ApplyAsync(
                RelationshipEvent(
                    firstLegStream,
                    activeEventId,
                    1,
                    70,
                    80,
                    RelationshipKinds.Friend,
                    RelationshipStates.Active),
                Now.AddMinutes(3),
                cancellationToken);
            await store.ApplyAsync(
                RelationshipEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1,
                    80,
                    90,
                    RelationshipKinds.Friend,
                    RelationshipStates.Active),
                Now.AddMinutes(3),
                cancellationToken);
            var activity = FeedActivity.Create(
                "post",
                "revocable-fof",
                "created",
                70,
                FeedAudiencePolicies.FriendsOfFriends,
                Now.AddMinutes(3),
                Now.AddMinutes(3));
            context.FeedActivities.Add(activity);
            await context.SaveChangesAsync(cancellationToken);
            var entry = FeedEntry.Create(
                activity,
                90,
                FeedDeliveryModes.Push,
                activity.RankKey,
                Now.AddMinutes(3));
            context.FeedEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken);

            await store.ApplyAsync(
                RelationshipEvent(
                    firstLegStream,
                    revocationEventId,
                    2,
                    70,
                    80,
                    RelationshipKinds.Friend,
                    RelationshipStates.Revoked),
                Now.AddMinutes(4),
                cancellationToken);
            var revocationSourceId = $"fof:{revocationEventId:N}:70:80";
            var persistedRevocation = await context.FeedRevocationWorkItems
                .AsNoTracking()
                .SingleAsync(
                    work => work.SourceEventId == revocationSourceId,
                    cancellationToken);
            Assert.Equal(
                revocationSourceOccurredAtUtc,
                persistedRevocation.SourceOccurredAtUtc);
            Assert.Equal(Now.AddMinutes(4), persistedRevocation.CreatedAtUtc);

            context.ChangeTracker.Clear();
            var revocation = new FeedRevocationProcessor(
                context,
                erasureAuthority,
                Options.Create(new FeedOptions()),
                audienceOptions,
                new FeedTelemetry(),
                new FixedTimeProvider(Now.AddMinutes(5)));
            await revocation.ProcessBatchAsync(
                "test-worker",
                Now.AddMinutes(5),
                cancellationToken);
            Assert.NotNull((await context.FeedEntries
                .AsNoTracking()
                .SingleAsync(item => item.Id == entry.Id, cancellationToken)).RevokedAtUtc);
            Assert.Equal(
                Now.AddMinutes(5),
                (await context.FeedRevocationWorkItems
                    .AsNoTracking()
                    .SingleAsync(
                        work => work.SourceEventId == revocationSourceId,
                        cancellationToken))
                    .CompletedAtUtc);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static Task<AudienceAccessDecision> Evaluate(
        AudienceProjectionStore store,
        int owner,
        int viewer,
        string policy,
        CancellationToken cancellationToken) =>
        store.EvaluateAsync(
            owner,
            viewer,
            policy,
            Now,
            TimeSpan.FromHours(1),
            cancellationToken);

    private static FeedAudienceBootstrapState CompleteBootstrap(DateTime observedAtUtc)
    {
        var state = FeedAudienceBootstrapState.Create(observedAtUtc);
        CompleteExistingBootstrap(state, observedAtUtc);
        return state;
    }

    private static void CompleteExistingBootstrap(
        FeedAudienceBootstrapState state,
        DateTime observedAtUtc)
    {
        var lease = state.TryLease("test-bootstrap", observedAtUtc, TimeSpan.FromMinutes(1));
        Assert.NotNull(lease);
        state.BeginCycle(lease.Value, observedAtUtc);
        state.ApplyPage(
            lease.Value,
            0,
            DateTime.UnixEpoch,
            0,
            hasMore: false,
            observedAtUtc);
    }

    private static void AddRelationship(
        FeedContext context,
        int owner,
        int viewer,
        string kind,
        string state,
        DateTime? observedAtUtc = null)
    {
        var observed = observedAtUtc ?? Now;
        var streamId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var checkpoint = FeedRelationshipCheckpoint.Create(
            streamId,
            owner,
            viewer,
            kind,
            observed);
        checkpoint.Reconcile(eventId, 1, observed, observed);
        context.FeedRelationshipCheckpoints.Add(checkpoint);
        context.FeedAudienceEdges.Add(FeedAudienceEdge.Create(
            owner,
            viewer,
            kind,
            state,
            streamId,
            eventId,
            1,
            observed,
            observed));
    }

    private static RelationshipStateChanged RelationshipEvent(
        Guid streamId,
        Guid eventId,
        long version,
        int owner,
        int viewer,
        string kind,
        string state) =>
        new()
        {
            EventId = eventId,
            StreamId = streamId,
            StreamVersion = version,
            OwnerUserId = owner,
            ViewerUserId = viewer,
            RelationshipKind = kind,
            State = state,
            OccurredAtUtc = Now.AddMinutes(version),
            ReasonCode = "test",
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
