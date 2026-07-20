using Feed.Application.Features.Audience;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Infra.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerContracts.Following;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class RelationshipProjectionIntegrationTests
{
    private static readonly MariaDbServerVersion ServerVersion = new(new Version(10, 6, 0));

    [Fact]
    [Trait("Category", "Integration")]
    public Task Feed_projection_recovers_gaps_and_fails_closed_on_conflicts_or_staleness() =>
        InIsolatedDatabase("audience_feed", async (connectionString, cancellationToken) =>
        {
            var options = new DbContextOptionsBuilder<FeedContext>()
                .UseMySql(
                    connectionString,
                    ServerVersion,
                    provider => provider.CommandTimeout(15))
                .Options;
            await using var context = new FeedContext(options);
            await context.Database.MigrateAsync(cancellationToken);
            await context.FeedPublisherStates.AddAsync(
                FeedPublisherState.Create(10, 0, 100_000, 80_000, Utc(0)),
                cancellationToken);
            var bootstrap = FeedAudienceBootstrapState.Create(Utc(0));
            var bootstrapLease = bootstrap.TryLease(
                "integration-bootstrap",
                Utc(0),
                TimeSpan.FromMinutes(1));
            Assert.NotNull(bootstrapLease);
            bootstrap.BeginCycle(bootstrapLease.Value, Utc(0));
            bootstrap.ApplyPage(
                bootstrapLease.Value,
                snapshotHighWatermark: 0,
                snapshotAsOfUtc: Utc(0),
                nextCursor: 0,
                hasMore: false,
                observedAtUtc: Utc(0));
            await context.FeedAudienceBootstrapStates.AddAsync(
                bootstrap,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            var store = new AudienceProjectionStore(
                context,
                new FeedUserErasureAuthority(context),
                new AudienceProjectionTelemetry(),
                Options.Create(new AudienceProjectionOptions
                {
                    MaximumBootstrapPageSize = 50,
                    MaximumQuarantineDrain = 10,
                    MaximumCheckpointAgeSeconds = 3600,
                }),
                Options.Create(new Feed.Application.Features.Feeds.FeedOptions()),
                new FixedTimeProvider(Utc(0)));

            var streamId = Guid.NewGuid();
            var firstEventId = Guid.NewGuid();
            var secondEventId = Guid.NewGuid();
            var fourthEventId = Guid.NewGuid();
            var baseline = Utc(0);

            var gap = await store.ApplyAsync(
                Event(streamId, secondEventId, 2, RelationshipStates.Revoked, baseline.AddMinutes(2)),
                baseline.AddMinutes(2),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.GapQuarantined, gap.Outcome);

            var recovered = await store.ApplyAsync(
                Event(streamId, firstEventId, 1, RelationshipStates.Active, baseline.AddMinutes(1)),
                baseline.AddMinutes(3),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.Applied, recovered.Outcome);
            Assert.Equal(2, recovered.AppliedVersion);

            var checkpoint = await context.FeedRelationshipCheckpoints.SingleAsync(cancellationToken);
            var edge = await context.FeedAudienceEdges.SingleAsync(cancellationToken);
            var quarantine = await context.FeedRelationshipEventQuarantines.SingleAsync(cancellationToken);
            Assert.Equal(AudienceProjectionStates.Healthy, checkpoint.Status);
            Assert.Equal(2, checkpoint.LastAppliedVersion);
            Assert.Equal(RelationshipStates.Revoked, edge.State);
            Assert.Equal(2, edge.SourceVersion);
            Assert.NotNull(quarantine.ResolvedAtUtc);
            Assert.Equal("contiguous_replay", quarantine.Resolution);

            var oldDuplicate = await store.ApplyAsync(
                Event(streamId, firstEventId, 1, RelationshipStates.Active, baseline.AddMinutes(1)),
                baseline.AddMinutes(4),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.StaleOrDuplicate, oldDuplicate.Outcome);

            var replayConflict = await store.ApplyAsync(
                Event(streamId, Guid.NewGuid(), 2, RelationshipStates.Active, baseline.AddMinutes(2)),
                baseline.AddMinutes(5),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.InvalidQuarantined, replayConflict.Outcome);
            checkpoint = await context.FeedRelationshipCheckpoints.SingleAsync(cancellationToken);
            Assert.Equal(AudienceProjectionStates.Quarantined, checkpoint.Status);

            var repaired = await store.ReconcilePageAsync(
                [Snapshot(1, streamId, secondEventId, 2, RelationshipStates.Revoked, baseline.AddMinutes(2))],
                baseline.AddMinutes(6),
                cancellationToken);
            Assert.Equal(1, repaired);

            var laterGap = await store.ApplyAsync(
                Event(streamId, fourthEventId, 4, RelationshipStates.Active, baseline.AddMinutes(8)),
                baseline.AddMinutes(8),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.GapQuarantined, laterGap.Outcome);
            var deniedDuringGap = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Followers,
                baseline.AddMinutes(9),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.False(deniedDuringGap.Allowed);

            repaired = await store.ReconcilePageAsync(
                [Snapshot(1, streamId, fourthEventId, 4, RelationshipStates.Active, baseline.AddMinutes(8))],
                baseline.AddMinutes(10),
                cancellationToken);
            Assert.Equal(1, repaired);
            var deniedUntilBootstrap = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Followers,
                baseline.AddMinutes(11),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.False(deniedUntilBootstrap.Allowed);
            Assert.Equal("bootstrap_incomplete", deniedUntilBootstrap.ReasonCode);

            var recoveryBootstrap = await context.FeedAudienceBootstrapStates
                .SingleAsync(cancellationToken);
            var recoveryLease = recoveryBootstrap.TryLease(
                "integration-recovery",
                baseline.AddMinutes(11),
                TimeSpan.FromMinutes(1));
            Assert.NotNull(recoveryLease);
            recoveryBootstrap.BeginCycle(recoveryLease.Value, baseline.AddMinutes(11));
            recoveryBootstrap.ApplyPage(
                recoveryLease.Value,
                snapshotHighWatermark: 0,
                snapshotAsOfUtc: baseline.AddMinutes(11),
                nextCursor: 0,
                hasMore: false,
                observedAtUtc: baseline.AddMinutes(11));
            await context.SaveChangesAsync(cancellationToken);

            var allowed = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Followers,
                baseline.AddMinutes(11),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.True(allowed.Allowed, allowed.ReasonCode);
            Assert.Equal(
                1,
                (await context.FeedPublisherStates.SingleAsync(cancellationToken)).AudienceCount);

            var exactDuplicate = await store.ApplyAsync(
                Event(streamId, fourthEventId, 4, RelationshipStates.Active, baseline.AddMinutes(8)),
                baseline.AddMinutes(12),
                cancellationToken);
            Assert.Equal(RelationshipProjectionOutcome.StaleOrDuplicate, exactDuplicate.Outcome);

            var deniedWhenStale = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Followers,
                baseline.AddHours(2),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.False(deniedWhenStale.Allowed);

            var conflictingSnapshot = await store.ReconcilePageAsync(
                [Snapshot(1, streamId, Guid.NewGuid(), 4, RelationshipStates.Revoked, baseline.AddMinutes(8))],
                baseline.AddMinutes(13),
                cancellationToken);
            Assert.Equal(0, conflictingSnapshot);
            checkpoint = await context.FeedRelationshipCheckpoints.SingleAsync(
                item => item.StreamId == streamId,
                cancellationToken);
            Assert.Equal(AudienceProjectionStates.Quarantined, checkpoint.Status);

            var blockStreamId = Guid.NewGuid();
            await store.ApplyAsync(
                Event(
                    blockStreamId,
                    Guid.NewGuid(),
                    1,
                    RelationshipStates.Blocked,
                    baseline.AddMinutes(14),
                    RelationshipKinds.Block),
                baseline.AddMinutes(14),
                cancellationToken);
            var publicBlocked = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Public,
                baseline.AddMinutes(15),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.False(publicBlocked.Allowed);

            await store.ApplyAsync(
                Event(
                    blockStreamId,
                    Guid.NewGuid(),
                    2,
                    RelationshipStates.Revoked,
                    baseline.AddMinutes(16),
                    RelationshipKinds.Block),
                baseline.AddMinutes(16),
                cancellationToken);
            var publicAllowed = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Public,
                baseline.AddMinutes(17),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.True(publicAllowed.Allowed);

            var reverseBlockStreamId = Guid.NewGuid();
            await store.ApplyAsync(
                Event(
                    reverseBlockStreamId,
                    Guid.NewGuid(),
                    1,
                    RelationshipStates.Blocked,
                    baseline.AddMinutes(18),
                    RelationshipKinds.Block,
                    ownerUserId: 20,
                    viewerUserId: 10),
                baseline.AddMinutes(18),
                cancellationToken);
            var reverseDirectionBlocked = await store.EvaluateAsync(
                10,
                20,
                AudiencePolicies.Public,
                baseline.AddMinutes(19),
                TimeSpan.FromHours(1),
                cancellationToken);
            Assert.False(reverseDirectionBlocked.Allowed);

            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ApplyAsync(
                Event(
                    blockStreamId,
                    Guid.NewGuid(),
                    3,
                    RelationshipStates.Blocked,
                    baseline.AddMinutes(20),
                    RelationshipKinds.Block),
                baseline.AddMinutes(20),
                canceled.Token));
        });

    [Fact]
    [Trait("Category", "Integration")]
    public Task Following_relationship_stream_and_bus_message_commit_or_rollback_together() =>
        InIsolatedDatabase("relationship_following", async (connectionString, cancellationToken) =>
        {
            await using var provider = BuildFollowingServices(connectionString);

            await using (var migrationScope = provider.CreateAsyncScope())
            {
                var migrationContext = migrationScope.ServiceProvider.GetRequiredService<FollowingContext>();
                await migrationContext.Database.MigrateAsync(cancellationToken);
            }

            await using (var rollbackScope = provider.CreateAsyncScope())
            {
                var context = rollbackScope.ServiceProvider.GetRequiredService<FollowingContext>();
                var writer = rollbackScope.ServiceProvider.GetRequiredService<IRelationshipEventWriter>();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var staged = await writer.StageAsync(
                    Transition(RelationshipStates.Active, "follow.created"),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                Assert.Equal(1, await context.RelationshipEventStreams.CountAsync(cancellationToken));
                Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(cancellationToken));
                Assert.Equal(1, staged.StreamVersion);
                await transaction.RollbackAsync(cancellationToken);
            }

            await using (var rollbackVerificationScope = provider.CreateAsyncScope())
            {
                var context = rollbackVerificationScope.ServiceProvider.GetRequiredService<FollowingContext>();
                Assert.Equal(0, await context.RelationshipEventStreams.CountAsync(cancellationToken));
                Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync(cancellationToken));
            }

            RelationshipStateChanged[] transitions;
            await using (var commitScope = provider.CreateAsyncScope())
            {
                var context = commitScope.ServiceProvider.GetRequiredService<FollowingContext>();
                var writer = commitScope.ServiceProvider.GetRequiredService<IRelationshipEventWriter>();
                transitions =
                [
                    await StageAndSave(writer, context, RelationshipStates.Active, "follow.created", cancellationToken),
                    await StageAndSave(writer, context, RelationshipStates.Revoked, "follow.removed", cancellationToken),
                    await StageAndSave(writer, context, RelationshipStates.Active, "follow.recreated", cancellationToken),
                ];

                Assert.Equal([1L, 2L, 3L], transitions.Select(item => item.StreamVersion).ToArray());
                Assert.Single(transitions.Select(item => item.StreamId).Distinct());
                Assert.Equal(3, await context.Set<OutboxMessage>().CountAsync(cancellationToken));

                var stream = await context.RelationshipEventStreams.SingleAsync(cancellationToken);
                Assert.Equal(3, stream.CurrentVersion);
                Assert.Equal(RelationshipStates.Active, stream.CurrentState);

                var page = await new RelationshipSnapshotReader(context).ReadPageAsync(
                    afterCursor: 0,
                    pageSize: 1,
                    snapshotHighWatermark: null,
                    cancellationToken);
                var snapshot = Assert.Single(page.Items);
                Assert.Equal(stream.StreamId, snapshot.StreamId);
                Assert.Equal(3, snapshot.StreamVersion);
                Assert.Equal(transitions[^1].EventId, snapshot.LastEventId);
                Assert.False(page.HasMore);
            }

            await using var cancellationScope = provider.CreateAsyncScope();
            var cancellationWriter = cancellationScope.ServiceProvider
                .GetRequiredService<IRelationshipEventWriter>();
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellationWriter.StageAsync(
                Transition(RelationshipStates.Revoked, "follow.canceled"),
                canceled.Token));
        });

    private static async Task<RelationshipStateChanged> StageAndSave(
        IRelationshipEventWriter writer,
        FollowingContext context,
        string state,
        string reason,
        CancellationToken cancellationToken)
    {
        var staged = await writer.StageAsync(Transition(state, reason), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return staged;
    }

    private static ServiceProvider BuildFollowingServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FollowingContext>(options => options.UseMySql(
            connectionString,
            ServerVersion,
            provider => provider.CommandTimeout(15)));
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
                bus.Host("localhost", 5672, "relationship-outbox-tests", host =>
                {
                    host.Username("guest");
                    host.Password("guest");
                });
            });
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static RelationshipTransition Transition(string state, string reason) =>
        new(
            OwnerUserId: 10,
            ViewerUserId: 20,
            RelationshipKind: RelationshipKinds.Follow,
            State: state,
            ReasonCode: reason);

    private static RelationshipStateChanged Event(
        Guid streamId,
        Guid eventId,
        long version,
        string state,
        DateTime occurredAtUtc,
        string kind = RelationshipKinds.Follow,
        int ownerUserId = 10,
        int viewerUserId = 20) =>
        new()
        {
            EventId = eventId,
            StreamId = streamId,
            StreamVersion = version,
            OwnerUserId = ownerUserId,
            ViewerUserId = viewerUserId,
            RelationshipKind = kind,
            State = state,
            OccurredAtUtc = occurredAtUtc,
            ReasonCode = "integration.test",
        };

    private static RelationshipStateSnapshot Snapshot(
        long cursor,
        Guid streamId,
        Guid lastEventId,
        long version,
        string state,
        DateTime asOfUtc) =>
        new()
        {
            Cursor = cursor,
            StreamId = streamId,
            LastEventId = lastEventId,
            StreamVersion = version,
            OwnerUserId = 10,
            ViewerUserId = 20,
            RelationshipKind = RelationshipKinds.Follow,
            State = state,
            AsOfUtc = asOfUtc,
        };

    private static async Task InIsolatedDatabase(
        string prefix,
        Func<string, CancellationToken, Task> test)
    {
        var configuredConnection = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredConnection))
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");

        var databaseName = $"{prefix}_{Guid.NewGuid():N}";
        Assert.StartsWith($"{prefix}_", databaseName, StringComparison.Ordinal);
        var databaseConnection = WithDatabase(configuredConnection!, databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseCreated = false;

        try
        {
            await ExecuteAdministrativeCommand(
                configuredConnection!,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                timeout.Token);
            databaseCreated = true;
            await test(databaseConnection, timeout.Token);
        }
        finally
        {
            if (databaseCreated)
            {
                await ExecuteAdministrativeCommand(
                    configuredConnection!,
                    $"DROP DATABASE `{databaseName}`;",
                    CancellationToken.None);
            }
        }
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteAdministrativeCommand(
        string connectionString,
        string commandText,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = string.Empty,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection)
        {
            CommandTimeout = 15,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTime Utc(int minutes) =>
        new(2026, 7, 17, 12, minutes, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
