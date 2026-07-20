using Feed.Application.Features.Audience;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Domain.AggregatesModel.Moderation;
using Feed.Infra.Persistence;
using Feed.Infra.Persistence.Moderation;
using Inspacy.Caching;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace Feed.IntegrationTests;

public sealed class FeedModerationEnforcementMariaDbTests
{
    private static readonly DateTime Now =
        new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Moderation_projection_is_fail_closed_ordered_idempotent_and_atomic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await OpenDatabaseAsync(cancellationToken);
        if (database is null)
            return;

        await using var context = database.Context;
        try
        {
            Assert.Contains(
                "20260722000000_FeedModerationEnforcementProjection",
                await context.Database.GetAppliedMigrationsAsync(cancellationToken));

            var resourceId = $"moderated-{Guid.NewGuid():N}";
            var activity = FeedActivity.Create(
                FeedModerationResourceKinds.Post,
                resourceId,
                "created",
                actorUserId: 101,
                FeedAudiencePolicies.Public,
                publishedAtUtc: Now,
                createdAtUtc: Now,
                deliveryMode: FeedDeliveryModes.Push);
            context.FeedActivities.Add(activity);
            await context.SaveChangesAsync(cancellationToken);
            context.FeedEntries.Add(FeedEntry.Create(
                activity,
                recipientUserId: 101,
                FeedDeliveryModes.Push,
                activity.RankKey,
                Now));
            await context.SaveChangesAsync(cancellationToken);

            var reader = CreateReader(context);
            Assert.Single((await reader.ReadAsync(
                101,
                new FeedPageRequest(null, 20, IncludeSeen: true),
                cancellationToken)).Items);

            var caseId = Guid.NewGuid();
            var repository = new FeedModerationDecisionRepository(
                context,
                new FixedTimeProvider(Now.AddSeconds(10)));
            var versionTwo = FeedModerationDecisionInput.Create(
                caseId,
                FeedModerationResourceKinds.Post,
                resourceId,
                FeedModerationActions.RemoveContent,
                decisionVersion: 2,
                Now.AddSeconds(2));

            await repository.ApplyAsync(versionTwo, cancellationToken);
            context.ChangeTracker.Clear();

            var gapped = await context.FeedModerationDecisionStates
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(0, gapped.LastAppliedVersion);
            Assert.Equal(2, gapped.HighestObservedVersion);
            Assert.Equal(FeedModerationProjectionStates.Gap, gapped.Status);
            Assert.True(gapped.IsSuppressed);
            Assert.Single(await context.FeedModerationDecisionGaps
                .AsNoTracking()
                .ToListAsync(cancellationToken));
            Assert.Single(await context.FeedRevocationWorkItems
                .AsNoTracking()
                .ToListAsync(cancellationToken));
            Assert.Empty((await reader.ReadAsync(
                101,
                new FeedPageRequest(null, 20, IncludeSeen: true),
                cancellationToken)).Items);

            await repository.ApplyAsync(versionTwo, cancellationToken);
            Assert.Single(await context.FeedRevocationWorkItems
                .AsNoTracking()
                .ToListAsync(cancellationToken));

            await repository.ApplyAsync(
                FeedModerationDecisionInput.Create(
                    caseId,
                    FeedModerationResourceKinds.Post,
                    resourceId,
                    FeedModerationActions.NoAction,
                    decisionVersion: 1,
                    Now.AddSeconds(1)),
                cancellationToken);
            context.ChangeTracker.Clear();

            var recovered = await context.FeedModerationDecisionStates
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(2, recovered.LastAppliedVersion);
            Assert.Equal(FeedModerationProjectionStates.Healthy, recovered.Status);
            Assert.Equal(FeedModerationActions.RemoveContent, recovered.Action);
            Assert.True(recovered.IsSuppressed);
            Assert.Empty(await context.FeedModerationDecisionGaps
                .AsNoTracking()
                .ToListAsync(cancellationToken));
            Assert.Single(await context.FeedRevocationWorkItems
                .AsNoTracking()
                .ToListAsync(cancellationToken));

            await repository.ApplyAsync(
                FeedModerationDecisionInput.Create(
                    caseId,
                    FeedModerationResourceKinds.Post,
                    resourceId,
                    FeedModerationActions.NoAction,
                    decisionVersion: 3,
                    Now.AddSeconds(3)),
                cancellationToken);
            context.ChangeTracker.Clear();

            var released = await context.FeedModerationDecisionStates
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.False(released.IsSuppressed);
            Assert.Equal(3, released.LastAppliedVersion);
            Assert.Single(await context.FanoutWorkItems
                .AsNoTracking()
                .ToListAsync(cancellationToken));
            Assert.Single((await reader.ReadAsync(
                101,
                new FeedPageRequest(null, 20, IncludeSeen: true),
                cancellationToken)).Items);

            var rolledBackCaseId = Guid.NewGuid();
            var rolledBackResourceId = $"rolled-back-{Guid.NewGuid():N}";
            var rolledBackActivity = FeedActivity.Create(
                FeedModerationResourceKinds.Post,
                rolledBackResourceId,
                "created",
                actorUserId: 102,
                FeedAudiencePolicies.Public,
                publishedAtUtc: Now,
                createdAtUtc: Now);
            context.FeedActivities.Add(rolledBackActivity);
            await context.SaveChangesAsync(cancellationToken);
            var rolledBackActivityId = rolledBackActivity.Id;

            await using (var transaction =
                         await context.Database.BeginTransactionAsync(cancellationToken))
            {
                await repository.ApplyAsync(
                    FeedModerationDecisionInput.Create(
                        rolledBackCaseId,
                        FeedModerationResourceKinds.Post,
                        rolledBackResourceId,
                        FeedModerationActions.RemoveContent,
                        decisionVersion: 1,
                        Now.AddSeconds(4)),
                    cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
            }
            context.ChangeTracker.Clear();

            Assert.False(await context.FeedModerationDecisionStates
                .AsNoTracking()
                .AnyAsync(state => state.CaseId == rolledBackCaseId, cancellationToken));
            Assert.False(await context.FeedRevocationWorkItems
                .AsNoTracking()
                .AnyAsync(work => work.ActivityId == rolledBackActivityId, cancellationToken));
        }
        finally
        {
            await database.DeleteAsync(cancellationToken);
        }
    }

    private static FeedReader CreateReader(FeedContext context) => new(
        context,
        new FeedUserErasureAuthority(context),
        new PassThroughCache(),
        new CacheKeyFactory(
            new CacheNamespace("tests", "feed-moderation"),
            Options.Create(new InspacyHybridCacheOptions())),
        Options.Create(new FeedOptions { EnableFirstPageCache = false }),
        Options.Create(new AudienceProjectionOptions()),
        Options.Create(new FeedCursorOptions
        {
            ActiveKeyId = "primary",
            SigningKeys = new Dictionary<string, string>
            {
                ["primary"] = Convert.ToBase64String(
                    Enumerable.Repeat((byte)0x42, 32).ToArray()),
            },
        }),
        new FeedTelemetry(),
        new FixedTimeProvider(Now.AddMinutes(1)));

    private static async Task<TestDatabase?> OpenDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip(
                "Set TEST_MARIADB_CONNECTION to run Feed moderation MariaDB integration tests.");
            return null;
        }

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"feed_moderation_{Guid.NewGuid():N}",
        };
        var database = new TestDatabase(builder.ConnectionString);
        await database.Context.Database.MigrateAsync(cancellationToken);
        return database;
    }

    private sealed class TestDatabase(string connectionString)
    {
        public FeedContext Context { get; } = Create(connectionString);

        public async Task DeleteAsync(CancellationToken cancellationToken)
        {
            await using var cleanup = Create(connectionString);
            await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }

        private static FeedContext Create(string value) => new(
            new DbContextOptionsBuilder<FeedContext>()
                .UseMySql(value, new MariaDbServerVersion(new Version(10, 6, 27)))
                .Options);
    }

    private sealed class PassThroughCache : ISharedCache
    {
        public ValueTask<T> GetOrCreateAsync<T>(
            CacheKey key,
            Func<CancellationToken, ValueTask<T>> authoritativeSource,
            CacheEntryPolicy? policy = null,
            CancellationToken cancellationToken = default)
            where T : notnull => authoritativeSource(cancellationToken);

        public ValueTask RemoveAsync(
            CacheKey key,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
