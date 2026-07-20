using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Features.Reactions.Commands;
using Interaction.Application.Options;
using Interaction.Application.Providers.Cache;
using Interaction.Application.Providers.Events;
using Interaction.Application.Providers.Execution;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Infraestructure.Database.Ef;
using Interaction.Infraestructure.Database.Ef.Repositories;
using Interaction.Infraestructure.Providers.Events;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;
using ReactionKind = Interaction.Domain.AggregatesModel.ReactionKind;
using ResourceLifecycle = Interaction.Domain.AggregatesModel.ResourceLifecycle;
using ResourceVisibility = Interaction.Domain.AggregatesModel.ResourceVisibility;

namespace Interaction.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InteractionConcurrencyMariaDbCollection
{
    public const string Name = "interaction-mariadb-concurrency";
}

[Collection(InteractionConcurrencyMariaDbCollection.Name)]
public sealed class InteractionConcurrencyMariaDbTests
{
    private const string ConnectionEnvironment = "INTERACTION_MARIADB_TEST_CONNECTION";
    private const int WorkerCount = 100;
    private static readonly DateTimeOffset Now =
        new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task One_hundred_identical_reactions_have_one_effect_and_stale_loser_is_atomic()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionEnvironment);
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Skip($"Set {ConnectionEnvironment} to run the isolated Interaction MariaDB concurrency test.");
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
            var resource = new ResourceRef(
                ResourceType.Comment,
                $"concurrency-{Guid.NewGuid():N}");
            var state = new ResourceState(resource, Now);
            Assert.True(state.ApplyBaseline(
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                sourceVersion: 1,
                Now));
            setup.ResourceStates.Add(state);
            await setup.SaveChangesAsync(cancellationToken);

            var actorId = NewPositiveId();
            var publisher = new CountingPublisher();
            using var ready = new CountdownEvent(WorkerCount);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = Enumerable.Range(0, WorkerCount)
                .Select(_ => RunReactionAsync(
                    connectionString,
                    resource,
                    actorId,
                    publisher,
                    ready,
                    release.Task,
                    cancellationToken))
                .ToArray();

            Assert.True(
                ready.Wait(TimeSpan.FromSeconds(30), cancellationToken),
                "All 100 Interaction workers must reach the simultaneous start gate.");
            release.SetResult(true);
            var outcomes = await Task.WhenAll(workers)
                .WaitAsync(cancellationToken);

            Assert.Equal(1, outcomes.Count(applied => applied));
            Assert.Equal(WorkerCount - 1, outcomes.Count(applied => !applied));
            Assert.Equal(1, publisher.ReactionCount);
            Assert.Equal(1, publisher.SummaryCount);

            await using (var verification = CreateContext(connectionString))
            {
                var reaction = await verification.Reactions.AsNoTracking()
                    .SingleAsync(item =>
                        item.ResourceType == resource.Type
                        && item.ResourceId == resource.Id
                        && item.ActorId == actorId,
                        cancellationToken);
                var summary = await verification.EngagementSummaries.AsNoTracking()
                    .SingleAsync(item =>
                        item.ResourceType == resource.Type
                        && item.ResourceId == resource.Id,
                        cancellationToken);

                Assert.Equal(ReactionKind.Like, reaction.Kind);
                Assert.Equal(1, reaction.Version);
                Assert.Equal(1, summary.LikeCount);
                Assert.Equal(0, summary.DislikeCount);
                Assert.Equal(0, summary.RatingCount);
                Assert.Equal(0, summary.RatingTotal);
                Assert.Equal(1, summary.Version);
                Assert.All(
                    new[]
                    {
                        summary.LikeCount,
                        summary.DislikeCount,
                        summary.RatingCount,
                        summary.RatingTotal,
                        summary.ViewCount,
                        summary.CommentCount,
                        summary.ShareCount,
                    },
                    value => Assert.True(value >= 0));
            }

            Assert.Equal(
                "ResourceType,ResourceId,ActorId",
                await ReadUniqueIndexColumnsAsync(
                    connectionString,
                    "Reactions",
                    "PRIMARY",
                    cancellationToken));
            Assert.Equal(
                "ResourceType,ResourceId",
                await ReadUniqueIndexColumnsAsync(
                    connectionString,
                    "EngagementSummaries",
                    "PRIMARY",
                    cancellationToken));

            var losingMessageId = await ProveStaleWriterRollbackAsync(
                connectionString,
                resource,
                actorId,
                cancellationToken);

            await using (var final = CreateContext(connectionString))
            {
                var reaction = await final.Reactions.AsNoTracking()
                    .SingleAsync(item =>
                        item.ResourceType == resource.Type
                        && item.ResourceId == resource.Id
                        && item.ActorId == actorId,
                        cancellationToken);
                var summary = await final.EngagementSummaries.AsNoTracking()
                    .SingleAsync(item =>
                        item.ResourceType == resource.Type
                        && item.ResourceId == resource.Id,
                        cancellationToken);

                Assert.Equal(ReactionKind.Dislike, reaction.Kind);
                Assert.Equal(2, reaction.Version);
                Assert.Equal(0, summary.LikeCount);
                Assert.Equal(1, summary.DislikeCount);
                Assert.Equal(2, summary.Version);
                Assert.False(await final.Set<OutboxMessage>().AsNoTracking()
                    .AnyAsync(
                        message => message.MessageId == losingMessageId,
                        cancellationToken));
            }
        }
        finally
        {
            using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync(cleanupBudget.Token);
        }
    }

    private static async Task<bool> RunReactionAsync(
        string connectionString,
        ResourceRef resource,
        int actorId,
        CountingPublisher publisher,
        CountdownEvent ready,
        Task release,
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext(connectionString);
        var execution = CreateExecutionContext(context, publisher);
        var command = new SetReactionCommand(
            execution,
            new FixedIdentity(actorId));

        ready.Signal();
        await release.WaitAsync(cancellationToken);
        return await command.Handle(
            new SetReactionDto(resource, ReactionKind.Like),
            cancellationToken);
    }

    private static async Task<Guid> ProveStaleWriterRollbackAsync(
        string connectionString,
        ResourceRef resource,
        int actorId,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddDbContext<InteractionDbContext>(options => options.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(10, 6, 27))));
        services.AddScoped<IInteractionEventPublisher, InteractionEventPublisher>();
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<InteractionDbContext>(outbox =>
            {
                outbox.UseMySql();
                outbox.UseBusOutbox();
            });
            registration.UsingInMemory((_, _) => { });
        });

        await using var provider = services.BuildServiceProvider();
        await using var winningScope = provider.CreateAsyncScope();
        await using var losingScope = provider.CreateAsyncScope();
        var winningContext = winningScope.ServiceProvider
            .GetRequiredService<InteractionDbContext>();
        var losingContext = losingScope.ServiceProvider
            .GetRequiredService<InteractionDbContext>();

        var winningReaction = await winningContext.Reactions.SingleAsync(
            item => item.ResourceType == resource.Type
                && item.ResourceId == resource.Id
                && item.ActorId == actorId,
            cancellationToken);
        var winningSummary = await winningContext.EngagementSummaries.SingleAsync(
            item => item.ResourceType == resource.Type
                && item.ResourceId == resource.Id,
            cancellationToken);
        var losingReaction = await losingContext.Reactions.SingleAsync(
            item => item.ResourceType == resource.Type
                && item.ResourceId == resource.Id
                && item.ActorId == actorId,
            cancellationToken);
        var losingSummary = await losingContext.EngagementSummaries.SingleAsync(
            item => item.ResourceType == resource.Type
                && item.ResourceId == resource.Id,
            cancellationToken);

        Assert.True(winningReaction.SwitchTo(ReactionKind.Dislike, Now.AddSeconds(1)));
        winningSummary.ApplyReaction(
            ReactionKind.Like,
            ReactionKind.Dislike,
            Now.AddSeconds(1));
        await new InteractionRepository(winningContext)
            .SaveChangesAsync(cancellationToken);

        Assert.True(losingReaction.SwitchTo(ReactionKind.Dislike, Now.AddSeconds(2)));
        losingSummary.ApplyReaction(
            ReactionKind.Like,
            ReactionKind.Dislike,
            Now.AddSeconds(2));
        var losingMessageId = Guid.NewGuid();
        var losingPublisher = losingScope.ServiceProvider
            .GetRequiredService<IInteractionEventPublisher>();
        await losingPublisher.PublishReactionAsync(
            new ReactionChanged(
                losingMessageId,
                "comment",
                resource.Id,
                actorId,
                Interaction.Contracts.ReactionKind.Dislike,
                InteractionVersion: 2,
                Now.AddSeconds(2)),
            cancellationToken);

        var conflict = await Assert.ThrowsAsync<InteractionConflictException>(() =>
            new InteractionRepository(losingContext)
                .SaveChangesAsync(cancellationToken));
        Assert.Equal(409, conflict.StatusCode);
        return losingMessageId;
    }

    private static InteractionExecutionContext CreateExecutionContext(
        InteractionDbContext context,
        IInteractionEventPublisher publisher) => new(
        new InteractionRepository(context),
        publisher,
        PassThroughCache.Instance,
        new FixedTimeProvider(Now),
        Options.Create(new InteractionOptions()));

    private static InteractionDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options);

    private static string IsolatedConnection(string configured)
    {
        var builder = new MySqlConnectionStringBuilder(configured)
        {
            Database = $"interaction_concurrency_{Guid.NewGuid():N}",
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

    private sealed class FixedIdentity(int actorId) : IIdentityService
    {
        public AuthUser User { get; } = new(actorId, "interaction-concurrency");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PassThroughCache : IEngagementSummaryCache
    {
        public static PassThroughCache Instance { get; } = new();

        public ValueTask<EngagementSummaryDto> GetOrCreateAsync(
            ResourceRef resource,
            long version,
            Func<CancellationToken, ValueTask<EngagementSummaryDto>> authoritativeSource,
            CancellationToken cancellationToken = default) =>
            authoritativeSource(cancellationToken);

        public ValueTask InvalidateAsync(
            ResourceRef resource,
            long version,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class CountingPublisher : IInteractionEventPublisher
    {
        private int _reactionCount;
        private int _summaryCount;

        public int ReactionCount => Volatile.Read(ref _reactionCount);
        public int SummaryCount => Volatile.Read(ref _summaryCount);

        public Task PublishReactionAsync(
            ReactionChanged message,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reactionCount);
            return Task.CompletedTask;
        }

        public Task PublishRatingAsync(
            RatingChanged message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishSummaryAsync(
            EngagementSummaryChanged message,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _summaryCount);
            return Task.CompletedTask;
        }

        public Task PublishErasureCompletedAsync(
            UserErasureCompleted message,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
