using Feed.API.AppInterfaces.Queues;
using Feed.Application.Dtos;
using Feed.Application.Features.Engagement;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Database.Ef.Migrations;
using Feed.Infra.Persistence;
using Inspacy.Caching;
using Interaction.Contracts;
using MassTransit;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using MySqlConnector;
using System.Data.Common;
using System.Reflection;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedEngagementProjectionTests
{
    private static readonly DateTime Now =
        new(2026, 7, 17, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Engagement_resource_identity_is_strict_and_allow_listed()
    {
        Assert.Equal("comment", FeedEngagementResourceTypes.Normalize(" COMMENT "));
        Assert.Equal("post", FeedEngagementResourceTypes.Normalize("post"));
        Assert.Equal("resource-42", FeedEngagementResourceTypes.NormalizeId(" resource-42 "));

        Assert.Throws<ArgumentException>(() => FeedEngagementResourceTypes.Normalize("video"));
        Assert.Throws<ArgumentException>(() => FeedEngagementResourceTypes.NormalizeId("\t"));
        Assert.Throws<ArgumentException>(() => FeedEngagementResourceTypes.NormalizeId("bad\u0001id"));
        Assert.Throws<ArgumentException>(() => FeedEngagementResourceTypes.NormalizeId(
            new string('a', FeedEngagementResourceTypes.MaximumIdLength + 1)));
    }

    [Fact]
    public void Engagement_counters_enforce_nonnegative_values_and_the_one_to_six_rating_scale()
    {
        FeedEngagementSummary.ValidateCounters(0, 0, 0, 0, 0, 0, 0);
        FeedEngagementSummary.ValidateCounters(1, 2, 3, 18, 4, 5, 6);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FeedEngagementSummary.ValidateCounters(-1, 0, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() =>
            FeedEngagementSummary.ValidateCounters(0, 0, 0, 1, 0, 0, 0));
        Assert.Throws<ArgumentException>(() =>
            FeedEngagementSummary.ValidateCounters(0, 0, 2, 1, 0, 0, 0));
        Assert.Throws<ArgumentException>(() =>
            FeedEngagementSummary.ValidateCounters(0, 0, 2, 13, 0, 0, 0));
    }

    [Fact]
    public void Engagement_checkpoint_remains_unreadable_until_a_gap_is_recovered_in_order()
    {
        var checkpoint = FeedEngagementSummary.CreateCheckpoint("comment", "42", Now);
        Assert.Equal(FeedEngagementVersionDisposition.Gap, checkpoint.Classify(2));

        checkpoint.MarkGap(2, Now.AddSeconds(1));
        Assert.False(checkpoint.IsPubliclyReadable);
        Assert.Equal(1, checkpoint.GapFromVersion);

        checkpoint.ApplyNext(
            Guid.NewGuid(), 1, 0, 1, 5, 2, 0, 0, 1, Now, Now.AddSeconds(2));
        Assert.False(checkpoint.IsPubliclyReadable);
        Assert.Equal(2, checkpoint.HighestObservedVersion);

        checkpoint.ApplyNext(
            Guid.NewGuid(), 2, 1, 2, 9, 4, 1, 0, 2, Now.AddSeconds(1), Now.AddSeconds(3));
        Assert.True(checkpoint.IsPubliclyReadable);
        Assert.Equal(FeedEngagementProjectionStates.Healthy, checkpoint.Status);
        Assert.Equal(2, checkpoint.LastAppliedVersion);
    }

    [Fact]
    public void Engagement_model_has_dedupe_drain_and_health_indexes()
    {
        using var context = CreateModelContext();
        var summary = Assert.IsAssignableFrom<IEntityType>(
            context.Model.FindEntityType(typeof(FeedEngagementSummary)));
        Assert.Equal(
            [nameof(FeedEngagementSummary.ResourceType), nameof(FeedEngagementSummary.ResourceId)],
            summary.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        AssertIndex(summary, "UX_FeedEngagementSummary_LastEventId", unique: true);
        AssertIndex(summary, "IX_FeedEngagementSummary_Status_Updated", unique: false);
        Assert.True(summary.FindProperty(nameof(FeedEngagementSummary.Version))!.IsConcurrencyToken);

        var quarantine = Assert.IsAssignableFrom<IEntityType>(
            context.Model.FindEntityType(typeof(FeedEngagementSummaryEventQuarantine)));
        AssertIndex(quarantine, "UX_FeedEngagementSummaryEventQuarantine_EventId", unique: true);
        AssertIndex(quarantine, "IX_FeedEngagementSummaryEventQuarantine_Drain", unique: false);
    }

    [Fact]
    public void Engagement_migration_is_expand_only_and_creates_both_durable_tables()
    {
        var migration = new FeedEngagementSummaryProjection();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var up = typeof(FeedEngagementSummaryProjection).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(up);
        up.Invoke(migration, [builder]);

        Assert.All(builder.Operations, operation => Assert.True(
            operation is CreateTableOperation or CreateIndexOperation,
            $"Unexpected non-expand operation: {operation.GetType().Name}"));
        Assert.Equal(
            ["FeedEngagementSummary", "FeedEngagementSummaryEventQuarantine"],
            builder.Operations.OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Consumer_maps_every_shared_contract_field_and_uses_a_dedicated_definition()
    {
        var occurred = new DateTimeOffset(Now);
        var message = new EngagementSummaryChanged(
            Guid.NewGuid(), "comment", "42", 1, 2, 3, 12, 4, 5, 6, 7, occurred);

        var mapped = EngagementSummaryChangedConsumer.Map(message);

        Assert.Equal(message.EventId, mapped.EventId);
        Assert.Equal(message.ResourceType, mapped.ResourceType);
        Assert.Equal(message.ResourceId, mapped.ResourceId);
        Assert.Equal(message.LikeCount, mapped.LikeCount);
        Assert.Equal(message.DislikeCount, mapped.DislikeCount);
        Assert.Equal(message.RatingCount, mapped.RatingCount);
        Assert.Equal(message.RatingTotal, mapped.RatingTotal);
        Assert.Equal(message.ViewCount, mapped.ViewCount);
        Assert.Equal(message.CommentCount, mapped.CommentCount);
        Assert.Equal(message.ShareCount, mapped.ShareCount);
        Assert.Equal(message.SummaryVersion, mapped.SummaryVersion);
        Assert.Equal(Now, mapped.SourceOccurredAtUtc);
        Assert.True(typeof(IConsumer<EngagementSummaryChanged>)
            .IsAssignableFrom(typeof(EngagementSummaryChangedConsumer)));
        Assert.True(typeof(ConsumerDefinition<EngagementSummaryChangedConsumer>)
            .IsAssignableFrom(typeof(EngagementSummaryChangedConsumerDefinition)));
    }

    [Fact]
    public async Task MariaDb_projection_recovers_gaps_and_cached_reads_suppress_unhealthy_summaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run the Feed engagement projection integration test.");

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = $"feed_engagement_{Guid.NewGuid():N}",
        };
        var queryCounter = new EngagementQueryCountingInterceptor();
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                builder.ConnectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .AddInterceptors(queryCounter)
            .Options;

        await using var context = new FeedContext(options);
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            var telemetry = new FeedTelemetry();
            var projection = new EngagementSummaryProjectionStore(context, telemetry);

            // There is no baseline snapshot in this contract. Seeing version 3 first must retain the
            // absolute event but expose nothing until versions 1 and 2 arrive and the chain is replayed.
            var thirdEventObservedFirst = Summary(
                Guid.NewGuid(),
                version: 3,
                likes: 30,
                ratingCount: 2,
                ratingTotal: 10,
                resourceType: "post",
                resourceId: "first-observed-gap");
            var firstObservedGap = await projection.ApplyAsync(
                thirdEventObservedFirst,
                Now,
                cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.GapRetained, firstObservedGap.Outcome);
            Assert.Equal(0, firstObservedGap.LastAppliedVersion);
            Assert.Equal(FeedEngagementProjectionStates.Gap, firstObservedGap.Status);
            var hiddenBaseline = await context.FeedEngagementSummaries
                .AsNoTracking()
                .SingleAsync(item => item.ResourceId == "first-observed-gap", cancellationToken);
            Assert.False(hiddenBaseline.IsPubliclyReadable);
            Assert.Equal(0, hiddenBaseline.LikeCount);

            var firstEvent = Summary(
                Guid.NewGuid(), 1, likes: 10, ratingCount: 1, ratingTotal: 5,
                resourceType: "post", resourceId: "first-observed-gap");
            var stillGapped = await projection.ApplyAsync(
                firstEvent,
                Now.AddSeconds(1),
                cancellationToken);
            Assert.Equal(FeedEngagementProjectionStates.Gap, stillGapped.Status);
            var secondEvent = Summary(
                Guid.NewGuid(), 2, likes: 20, ratingCount: 2, ratingTotal: 9,
                resourceType: "post", resourceId: "first-observed-gap");
            var baselineRecovered = await projection.ApplyAsync(
                secondEvent,
                Now.AddSeconds(2),
                cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.GapRecovered, baselineRecovered.Outcome);
            var recoveredBaseline = await context.FeedEngagementSummaries
                .AsNoTracking()
                .SingleAsync(item => item.ResourceId == "first-observed-gap", cancellationToken);
            Assert.True(recoveredBaseline.IsPubliclyReadable);
            Assert.Equal(3, recoveredBaseline.LastAppliedVersion);
            Assert.Equal(30, recoveredBaseline.LikeCount);

            var activity = FeedActivity.Create(
                "comment", "42", "created", actorUserId: 7,
                FeedAudiencePolicies.Public,
                publishedAtUtc: Now, createdAtUtc: Now,
                sourceStreamId: Guid.NewGuid(), eventId: Guid.NewGuid(), rankKey: 900);
            var secondActivity = FeedActivity.Create(
                "post", "99", "created", actorUserId: 7,
                FeedAudiencePolicies.Public,
                publishedAtUtc: Now.AddSeconds(-1), createdAtUtc: Now,
                sourceStreamId: Guid.NewGuid(), eventId: Guid.NewGuid(), rankKey: 800);
            context.FeedActivities.AddRange(activity, secondActivity);
            await context.SaveChangesAsync(cancellationToken);
            context.FeedEntries.AddRange(
                FeedEntry.Create(activity, 7, FeedDeliveryModes.Push, 900, Now),
                FeedEntry.Create(secondActivity, 7, FeedDeliveryModes.Push, 800, Now));
            await context.SaveChangesAsync(cancellationToken);

            var event1 = Summary(Guid.NewGuid(), version: 1, likes: 1, ratingCount: 1, ratingTotal: 5);
            var applied = await projection.ApplyAsync(event1, Now, cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.Applied, applied.Outcome);
            var postEvent1 = Summary(
                Guid.NewGuid(), 1, likes: 9, resourceType: "post", resourceId: "99");
            await projection.ApplyAsync(postEvent1, Now, cancellationToken);

            var cache = new RecordingCache();
            var cursorSecurity = Options.Create(new FeedCursorOptions
            {
                ActiveKeyId = "primary",
                SigningKeys = new Dictionary<string, string>
                {
                    ["primary"] = Convert.ToBase64String(
                        Enumerable.Repeat((byte)0x42, 32).ToArray()),
                },
            });
            var reader = new FeedReader(
                context,
                new FeedUserErasureAuthority(context),
                cache,
                new CacheKeyFactory(
                    new CacheNamespace("tests", "feed"),
                    Options.Create(new InspacyHybridCacheOptions())),
                Options.Create(new FeedOptions { EnableFirstPageCache = true }),
                Options.Create(new AudienceProjectionOptions()),
                cursorSecurity,
                telemetry,
                new FixedTimeProvider(Now.AddMinutes(1)));
            var request = new FeedPageRequest(null, 20, IncludeSeen: true);
            queryCounter.Reset();
            var first = await reader.ReadAsync(7, request, cancellationToken);
            Assert.Equal(2, first.Items.Count);
            var firstItem = Assert.Single(first.Items, item => item.TargetId == "42");
            Assert.Equal(900, firstItem.RankKey);
            Assert.Equal(1, firstItem.Engagement!.SummaryVersion);
            Assert.All(first.Items, item => Assert.NotNull(item.Engagement));
            Assert.Equal(1, queryCounter.EngagementSummaryQueries);
            Assert.Equal(1, cache.AuthoritativeLoads);

            var event3 = Summary(Guid.NewGuid(), version: 3, likes: 3, ratingCount: 2, ratingTotal: 10);
            var gapped = await projection.ApplyAsync(event3, Now.AddSeconds(3), cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.GapRetained, gapped.Outcome);
            Assert.Equal(FeedEngagementProjectionStates.Gap, gapped.Status);

            queryCounter.Reset();
            var duringGap = await reader.ReadAsync(7, request, cancellationToken);
            var gappedItem = Assert.Single(duringGap.Items, item => item.TargetId == "42");
            Assert.Null(gappedItem.Engagement);
            Assert.NotNull(Assert.Single(duringGap.Items, item => item.TargetId == "99").Engagement);
            Assert.Equal(firstItem.ActivityId, gappedItem.ActivityId);
            Assert.Equal(firstItem.RankKey, gappedItem.RankKey);
            Assert.Equal(1, queryCounter.EngagementSummaryQueries);
            Assert.Equal(1, cache.AuthoritativeLoads);

            var duplicate = await projection.ApplyAsync(event3, Now.AddSeconds(4), cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.StaleOrDuplicate, duplicate.Outcome);
            var event2 = Summary(Guid.NewGuid(), version: 2, likes: 2, ratingCount: 2, ratingTotal: 9);
            var recovered = await projection.ApplyAsync(event2, Now.AddSeconds(5), cancellationToken);
            Assert.Equal(EngagementProjectionOutcome.GapRecovered, recovered.Outcome);

            queryCounter.Reset();
            var afterRecovery = await reader.ReadAsync(7, request, cancellationToken);
            var recoveredItem = Assert.Single(afterRecovery.Items, item => item.TargetId == "42");
            Assert.Equal(3, recoveredItem.Engagement!.SummaryVersion);
            Assert.Equal(3, recoveredItem.Engagement.LikeCount);
            Assert.Equal(firstItem.RankKey, recoveredItem.RankKey);
            Assert.Equal(1, queryCounter.EngagementSummaryQueries);
            Assert.Equal(1, cache.AuthoritativeLoads);

            var cacheZero = new RecordingCache();
            var cacheZeroReader = new FeedReader(
                context,
                new FeedUserErasureAuthority(context),
                cacheZero,
                new CacheKeyFactory(
                    new CacheNamespace("tests", "feed-cache-zero"),
                    Options.Create(new InspacyHybridCacheOptions())),
                Options.Create(new FeedOptions { EnableFirstPageCache = false }),
                Options.Create(new AudienceProjectionOptions()),
                cursorSecurity,
                telemetry,
                new FixedTimeProvider(Now.AddMinutes(1)));
            var cursorPage1 = await cacheZeroReader.ReadAsync(
                7,
                new FeedPageRequest(null, 1, IncludeSeen: true),
                cancellationToken);
            Assert.Single(cursorPage1.Items);
            Assert.True(cursorPage1.HasMore);
            Assert.False(string.IsNullOrWhiteSpace(cursorPage1.NextCursor));
            var cursorPage2 = await cacheZeroReader.ReadAsync(
                7,
                new FeedPageRequest(cursorPage1.NextCursor, 1, IncludeSeen: true),
                cancellationToken);
            Assert.Single(cursorPage2.Items);
            Assert.NotEqual(cursorPage1.Items[0].ActivityId, cursorPage2.Items[0].ActivityId);
            Assert.True(cursorPage1.Items[0].RankKey > cursorPage2.Items[0].RankKey);
            Assert.Equal(0, cacheZero.AuthoritativeLoads);

            var checkpoint = await context.FeedEngagementSummaries
                .AsNoTracking()
                .SingleAsync(item => item.ResourceType == "comment" && item.ResourceId == "42", cancellationToken);
            Assert.Equal(FeedEngagementProjectionStates.Healthy, checkpoint.Status);
            Assert.Equal(3, checkpoint.LastAppliedVersion);
            var retained = await context.FeedEngagementSummaryEventQuarantines
                .AsNoTracking()
                .SingleAsync(item => item.ResourceType == "comment" && item.ResourceId == "42", cancellationToken);
            Assert.NotNull(retained.ResolvedAtUtc);
            Assert.Equal("gap_recovered", retained.Resolution);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Projection_rejects_invalid_payloads_and_honors_cancellation_before_io()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateModelContext();
        var store = new EngagementSummaryProjectionStore(context, new FeedTelemetry());

        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(
            Summary(Guid.NewGuid(), 1, resourceType: "video"), Now, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(
            Summary(Guid.NewGuid(), 1, resourceId: new string('x', 129)), Now, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ApplyAsync(
            Summary(Guid.NewGuid(), 1, likes: -1), Now, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(
            Summary(Guid.NewGuid(), 1, ratingCount: 1, ratingTotal: 7), Now, cancellationToken));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ApplyAsync(
            Summary(Guid.NewGuid(), 1), Now, cancellation.Token));
    }

    private static EngagementSummaryProjectionDto Summary(
        Guid eventId,
        long version,
        long likes = 0,
        long ratingCount = 0,
        long ratingTotal = 0,
        string resourceType = "comment",
        string resourceId = "42") =>
        new(
            eventId,
            resourceType,
            resourceId,
            likes,
            0,
            ratingCount,
            ratingTotal,
            0,
            0,
            0,
            version,
            Now.AddSeconds(version));

    private static FeedContext CreateModelContext()
    {
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                "Server=localhost;Database=feed_engagement_model;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new FeedContext(options);
    }

    private static void AssertIndex(IEntityType entity, string name, bool unique)
    {
        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == name);
        Assert.Equal(unique, index.IsUnique);
    }

    private sealed class RecordingCache : ISharedCache
    {
        private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);
        public int AuthoritativeLoads { get; private set; }

        public async ValueTask<T> GetOrCreateAsync<T>(
            CacheKey key,
            Func<CancellationToken, ValueTask<T>> authoritativeSource,
            CacheEntryPolicy? policy = null,
            CancellationToken cancellationToken = default)
            where T : notnull
        {
            if (_entries.TryGetValue(key.Value, out var existing))
                return (T)existing;
            AuthoritativeLoads++;
            var value = await authoritativeSource(cancellationToken);
            _entries[key.Value] = value;
            return value;
        }

        public ValueTask RemoveAsync(CacheKey key, CancellationToken cancellationToken = default)
        {
            _entries.Remove(key.Value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class EngagementQueryCountingInterceptor : DbCommandInterceptor
    {
        private int _engagementSummaryQueries;

        public int EngagementSummaryQueries => Volatile.Read(ref _engagementSummaryQueries);

        public void Reset() => Interlocked.Exchange(ref _engagementSummaryQueries, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count(command);
            return ValueTask.FromResult(result);
        }

        private void Count(DbCommand command)
        {
            if (command.CommandText.Contains(
                "FROM `FeedEngagementSummary` AS",
                StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _engagementSummaryQueries);
            }
        }
    }
}
