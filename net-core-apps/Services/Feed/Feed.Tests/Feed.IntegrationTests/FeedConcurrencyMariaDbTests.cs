using Feed.Application.Features.Privacy;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace Feed.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FeedConcurrencyMariaDbCollection
{
    public const string Name = "feed-mariadb-concurrency";
}

[Collection(FeedConcurrencyMariaDbCollection.Name)]
public sealed class FeedConcurrencyMariaDbTests
{
    private const string ConnectionEnvironment = "FEED_MARIADB_TEST_CONNECTION";
    private const int WorkerCount = 100;
    private static readonly DateTime Now =
        new(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task One_hundred_identical_canonical_events_have_one_activity_and_work_effect()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionEnvironment);
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Skip($"Set {ConnectionEnvironment} to run the isolated Feed MariaDB concurrency test.");
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var cancellationToken = deadline.Token;
        var connectionString = IsolatedConnection(configured);

        await using var setup = CreateContext(connectionString);
        try
        {
            await setup.Database.MigrateAsync(cancellationToken);
            await using var provider = CreateProvider(connectionString);

            var sourceEventId = Guid.NewGuid();
            var actorId = NewPositiveId();
            var targetId = $"concurrency-{Guid.NewGuid():N}";
            var projected = new FeedActivityChanged
            {
                EventId = sourceEventId,
                StreamId = Guid.NewGuid(),
                StreamVersion = 1,
                TargetType = "post",
                TargetId = targetId,
                ActivityKind = "created",
                ActorUserId = actorId,
                AudiencePolicy = FeedAudiencePolicies.Public,
                State = FeedActivityStates.Active,
                RankKey = Now.Ticks,
                PublishedAtUtc = Now,
                OccurredAtUtc = Now,
                ReasonCode = "created",
            };

            using var ready = new CountdownEvent(WorkerCount);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = Enumerable.Range(0, WorkerCount)
                .Select(_ => ApplyCanonicalConcurrentlyAsync(
                    provider,
                    projected,
                    ready,
                    release.Task,
                    cancellationToken))
                .ToArray();

            Assert.True(
                ready.Wait(TimeSpan.FromSeconds(30), cancellationToken),
                "All 100 Feed workers must reach the simultaneous start gate.");
            release.SetResult(true);
            await Task.WhenAll(workers)
                .WaitAsync(cancellationToken);
            await using (var verification = CreateContext(connectionString))
            {
                var activity = await verification.FeedActivities.AsNoTracking()
                    .SingleAsync(item => item.TargetId == targetId, cancellationToken);
                var work = await verification.FanoutWorkItems.AsNoTracking()
                    .SingleAsync(item => item.ActivityId == activity.Id, cancellationToken);

                Assert.Equal(sourceEventId, activity.LastEventId);
                Assert.Equal(projected.StreamId, activity.SourceStreamId);
                Assert.Equal(1, activity.SourceVersion);
                Assert.Equal(1, activity.Version);
                Assert.False(activity.IsTombstoned);
                Assert.Equal(sourceEventId.ToString("N"), work.SourceEventId);
                Assert.Equal(FeedWorkStates.Pending, work.State);
                Assert.Equal(0, work.AttemptCount);
                Assert.Equal(1, work.Version);
                Assert.Equal(1, await verification.FeedPublisherStates.AsNoTracking()
                    .CountAsync(state => state.PublisherUserId == actorId, cancellationToken));
                Assert.Equal(1, await verification.FeedUserMutationGuards.AsNoTracking()
                    .CountAsync(guard => guard.UserId == actorId, cancellationToken));
                Assert.Equal(0, await CountOrphanFanoutWorkAsync(
                    verification,
                    cancellationToken));
            }

            Assert.Equal(
                "TargetType,TargetId,ActivityKind",
                await ReadUniqueIndexColumnsAsync(
                    connectionString,
                    "FeedActivity",
                    "UX_FeedActivity_Target_Kind",
                    cancellationToken));
            Assert.Equal(
                "SourceStreamId",
                await ReadUniqueIndexColumnsAsync(
                    connectionString,
                    "FeedActivity",
                    "UX_FeedActivity_SourceStreamId",
                    cancellationToken));
            Assert.Equal(
                "SourceEventId",
                await ReadUniqueIndexColumnsAsync(
                    connectionString,
                    "FanoutWorkItem",
                    "UX_FanoutWorkItem_SourceEventId",
                    cancellationToken));

            await ProveDuplicateAndVersionConflictBehaviorAsync(
                provider,
                connectionString,
                projected,
                sourceEventId,
                cancellationToken);
            await ProveOuterRollbackHasNoOrphansAsync(
                provider,
                connectionString,
                cancellationToken);
        }
        finally
        {
            using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync(cleanupBudget.Token);
        }
    }

    private static async Task ApplyCanonicalConcurrentlyAsync(
        ServiceProvider provider,
        FeedActivityChanged message,
        CountdownEvent ready,
        Task release,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider
            .GetRequiredService<FeedActivityProjectionStore>();
        ready.Signal();
        await release.WaitAsync(cancellationToken);
        await store.ApplyCanonicalAsync(message, Now, cancellationToken);
    }

    private static async Task ProveDuplicateAndVersionConflictBehaviorAsync(
        ServiceProvider provider,
        string connectionString,
        FeedActivityChanged projected,
        Guid sourceEventId,
        CancellationToken cancellationToken)
    {
        await using (var duplicateScope = provider.CreateAsyncScope())
        {
            var store = duplicateScope.ServiceProvider
                .GetRequiredService<FeedActivityProjectionStore>();
            await store.ApplyCanonicalAsync(
                projected,
                Now.AddSeconds(1),
                cancellationToken);
        }

        var conflictingEventId = Guid.NewGuid();
        var conflict = projected with
        {
            EventId = conflictingEventId,
            ReasonCode = "same-version-conflict",
            OccurredAtUtc = Now.AddSeconds(2),
        };
        await using (var conflictScope = provider.CreateAsyncScope())
        {
            var store = conflictScope.ServiceProvider
                .GetRequiredService<FeedActivityProjectionStore>();
            await store.ApplyCanonicalAsync(
                conflict,
                Now.AddSeconds(2),
                cancellationToken);
            await store.ApplyCanonicalAsync(
                conflict,
                Now.AddSeconds(3),
                cancellationToken);
        }

        await using var verification = CreateContext(connectionString);
        var activity = await verification.FeedActivities.AsNoTracking()
            .SingleAsync(
                item => item.SourceStreamId == projected.StreamId,
                cancellationToken);
        Assert.True(activity.IsTombstoned);
        Assert.Equal(1, activity.SourceVersion);
        Assert.Equal(sourceEventId, activity.LastEventId);
        Assert.Equal(2, activity.Version);
        Assert.Equal(1, await verification.FeedActivityEventQuarantines.AsNoTracking()
            .CountAsync(item => item.EventId == conflictingEventId, cancellationToken));
        Assert.Equal(1, await verification.FeedRevocationWorkItems.AsNoTracking()
            .CountAsync(
                item => item.ActivityId == activity.Id,
                cancellationToken));
        Assert.Equal(1, await verification.FanoutWorkItems.AsNoTracking()
            .CountAsync(item => item.ActivityId == activity.Id, cancellationToken));
        Assert.Equal(0, await CountOrphanFanoutWorkAsync(
            verification,
            cancellationToken));
        Assert.Equal(0, await verification.FeedRevocationWorkItems.AsNoTracking()
            .CountAsync(
                work => !verification.FeedActivities.Any(
                    activityRow => activityRow.Id == work.ActivityId),
                cancellationToken));
    }

    private static async Task ProveOuterRollbackHasNoOrphansAsync(
        ServiceProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var rolledBackEventId = Guid.NewGuid();
        var rolledBackActorId = NewPositiveId();
        var rolledBackTargetId = $"rolled-back-{Guid.NewGuid():N}";
        var message = new FeedActivityChanged
        {
            EventId = rolledBackEventId,
            StreamId = Guid.NewGuid(),
            StreamVersion = 1,
            TargetType = "post",
            TargetId = rolledBackTargetId,
            ActivityKind = "created",
            ActorUserId = rolledBackActorId,
            AudiencePolicy = FeedAudiencePolicies.Public,
            State = FeedActivityStates.Active,
            RankKey = Now.AddMinutes(1).Ticks,
            PublishedAtUtc = Now.AddMinutes(1),
            OccurredAtUtc = Now.AddMinutes(1),
            ReasonCode = "created",
        };

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FeedContext>();
            var store = scope.ServiceProvider
                .GetRequiredService<FeedActivityProjectionStore>();
            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);
            await store.ApplyCanonicalAsync(
                message,
                Now.AddMinutes(1),
                cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
        }

        await using var verification = CreateContext(connectionString);
        Assert.False(await verification.FeedActivities.AsNoTracking()
            .AnyAsync(item => item.TargetId == rolledBackTargetId, cancellationToken));
        Assert.False(await verification.FanoutWorkItems.AsNoTracking()
            .AnyAsync(
                item => item.SourceEventId == rolledBackEventId.ToString("N"),
                cancellationToken));
        Assert.False(await verification.FeedPublisherStates.AsNoTracking()
            .AnyAsync(
                item => item.PublisherUserId == rolledBackActorId,
                cancellationToken));
        Assert.False(await verification.FeedUserMutationGuards.AsNoTracking()
            .AnyAsync(
                item => item.UserId == rolledBackActorId,
                cancellationToken));
        Assert.Equal(0, await CountOrphanFanoutWorkAsync(
            verification,
            cancellationToken));
    }

    private static Task<int> CountOrphanFanoutWorkAsync(
        FeedContext context,
        CancellationToken cancellationToken) =>
        context.FanoutWorkItems.AsNoTracking().CountAsync(
            work => !context.FeedActivities.Any(
                activity => activity.Id == work.ActivityId),
            cancellationToken);

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FeedContext>(options => options.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(10, 6, 27))));
        services.AddScoped<IFeedUserErasureAuthority, FeedUserErasureAuthority>();
        services.AddScoped<FeedActivityProjectionStore>();
        services.AddSingleton<IOptions<FeedOptions>>(
            Options.Create(new FeedOptions()));
        services.AddSingleton<FeedTelemetry>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        return services.BuildServiceProvider();
    }

    private static FeedContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options);

    private static string IsolatedConnection(string configured)
    {
        var builder = new MySqlConnectionStringBuilder(configured)
        {
            Database = $"feed_concurrency_{Guid.NewGuid():N}",
            MaximumPoolSize = 128,
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 30,
        };
        return builder.ConnectionString;
    }

    private static async Task<string?> ReadUniqueIndexColumnsAsync(
        string connectionString,
        string table,
        string index,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GROUP_CONCAT(`COLUMN_NAME` ORDER BY `SEQ_IN_INDEX` SEPARATOR ',')
            FROM `information_schema`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = @table
              AND `INDEX_NAME` = @index
              AND `NON_UNIQUE` = 0;
            """;
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        return Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static int NewPositiveId()
    {
        var value = BitConverter.ToInt32(Guid.NewGuid().ToByteArray())
            & int.MaxValue;
        return value == 0 ? 1 : value;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

}
