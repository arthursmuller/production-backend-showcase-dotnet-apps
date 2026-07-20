using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Features.Privacy.Commands;
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
using Xunit;
using ReactionKind = Interaction.Domain.AggregatesModel.ReactionKind;
using ResourceLifecycle = Interaction.Domain.AggregatesModel.ResourceLifecycle;
using ResourceVisibility = Interaction.Domain.AggregatesModel.ResourceVisibility;

namespace Interaction.UnitTests;

[Collection(InteractionMariaDbCollection.Name)]
public sealed class InteractionMariaDbIntegrationTests
{
    private const string ConnectionEnvironment =
        "INTERACTION_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Broker_offline_commit_preserves_erasure_and_rollback_removes_domain_and_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
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
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InteractionDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IInteractionEventPublisher>();
        await context.Database.MigrateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var committedUser = NewPositiveId();
        var committedRequest = Guid.NewGuid();
        var committedEvent = Guid.NewGuid();
        await using (var transaction =
                     await context.Database.BeginTransactionAsync(cancellationToken))
        {
            context.UserErasures.Add(InteractionUserErasure.Schedule(
                committedUser,
                committedRequest,
                1,
                now,
                now));
            await publisher.PublishErasureCompletedAsync(
                Completion(committedEvent, committedRequest, committedUser, now),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
        Assert.True(await context.UserErasures.AsNoTracking().AnyAsync(
            erasure => erasure.UserId == committedUser,
            cancellationToken));
        Assert.True(await context.Set<OutboxMessage>().AsNoTracking().AnyAsync(
            message => message.MessageId == committedEvent,
            cancellationToken));

        var rolledBackUser = NewPositiveId();
        var rolledBackRequest = Guid.NewGuid();
        var rolledBackEvent = Guid.NewGuid();
        await using (var transaction =
                     await context.Database.BeginTransactionAsync(cancellationToken))
        {
            context.UserErasures.Add(InteractionUserErasure.Schedule(
                rolledBackUser,
                rolledBackRequest,
                1,
                now,
                now));
            await publisher.PublishErasureCompletedAsync(
                Completion(rolledBackEvent, rolledBackRequest, rolledBackUser, now),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
        Assert.False(await context.UserErasures.AsNoTracking().AnyAsync(
            erasure => erasure.UserId == rolledBackUser,
            cancellationToken));
        Assert.False(await context.Set<OutboxMessage>().AsNoTracking().AnyAsync(
            message => message.MessageId == rolledBackEvent,
            cancellationToken));
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Projection_gap_backlog_uses_one_aggregate_store_view()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var db = new InteractionDbContext(dbOptions);
        await db.Database.MigrateAsync(cancellationToken);
        await ResetIsolatedDatabaseAsync(db, cancellationToken);

        var receivedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var resource = new ResourceRef(ResourceType.Comment, $"gap-{Guid.NewGuid():N}");
        db.AddRange(
            new ResourceState(resource, receivedAt),
            new ResourceStateGap(
                resource,
                2,
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                receivedAt,
                receivedAt));
        await db.SaveChangesAsync(cancellationToken);

        var repository = new InteractionRepository(db);
        var backlog = await repository.GetProjectionGapBacklogAsync(cancellationToken);
        Assert.Equal(1, backlog.PendingCount);
        Assert.Equal(receivedAt, backlog.OldestReceivedAtUtc);

        await db.ResourceStateGaps.ExecuteDeleteAsync(cancellationToken);
        backlog = await repository.GetProjectionGapBacklogAsync(cancellationToken);
        Assert.Equal(0, backlog.PendingCount);
        Assert.Null(backlog.OldestReceivedAtUtc);
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task User_erasure_is_bounded_permanent_and_does_not_head_of_line_block()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionEnvironment)!;
        var dbOptions = new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var db = new InteractionDbContext(dbOptions);
        await db.Database.MigrateAsync(cancellationToken);
        await ResetIsolatedDatabaseAsync(db, cancellationToken);

        var now = new DateTimeOffset(
            2030,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var publisher = new CapturingPublisher();
        var repository = new InteractionRepository(db);
        var erasures = new InteractionErasureRepository(db);
        var execution = new InteractionExecutionContext(
            repository,
            publisher,
            new PassthroughCache(),
            clock,
            Options.Create(new InteractionOptions
            {
                ErasureBatchSize = 2,
                ErasureLeaseSeconds = 30,
                ViewObservationRetentionDays = 1,
            }));
        var schedule = new ScheduleUserErasureCommand(execution, erasures);
        var process = new ProcessUserErasureCommand(execution, erasures);

        var erasedUser = NewPositiveId();
        var laterUser = NewPositiveId();
        var resources = new[]
        {
            new ResourceRef(ResourceType.Comment, $"erasure-{Guid.NewGuid():N}"),
            new ResourceRef(ResourceType.Comment, $"erasure-{Guid.NewGuid():N}"),
        };
        foreach (var resource in resources)
        {
            var state = new ResourceState(resource, now);
            Assert.True(state.ApplyBaseline(
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                1,
                now));
            var summary = new EngagementSummary(resource, now);
            summary.ApplyReaction(null, ReactionKind.Like, now);
            summary.ApplyRating(null, 5, now);
            db.AddRange(
                state,
                summary,
                new Reaction(resource, erasedUser, ReactionKind.Like, now),
                new Rating(resource, erasedUser, 5, "erase-me", now));
        }
        db.Add(new ViewObservation(
            resources[0],
            new string('a', 64),
            now,
            now,
            now.AddDays(1)));
        await db.SaveChangesAsync(cancellationToken);

        var erasedRequest = CreateRequest(erasedUser, now);
        var laterRequest = CreateRequest(laterUser, now.AddSeconds(1));
        Assert.True(await schedule.Handle(erasedRequest, cancellationToken));
        Assert.True(await schedule.Handle(laterRequest, cancellationToken));

        var blockedMutation = new SetReactionCommand(
            execution,
            new FixedIdentity(erasedUser));
        await Assert.ThrowsAsync<InteractionUnauthorizedException>(() =>
            blockedMutation.Handle(
                new SetReactionDto(resources[0], ReactionKind.Dislike),
                cancellationToken));

        var boundedResults = new List<int>();
        int changed;
        do
        {
            changed = await process.Handle(
                "integration-erasure-worker",
                cancellationToken);
            boundedResults.Add(changed);
        }
        while (changed > 0);

        Assert.All(boundedResults, result => Assert.InRange(result, 0, 3));
        Assert.False(await db.Reactions.AnyAsync(
            item => item.ActorId == erasedUser,
            cancellationToken));
        Assert.False(await db.Ratings.AnyAsync(
            item => item.ActorId == erasedUser,
            cancellationToken));
        Assert.Equal(
            InteractionUserErasureStage.AwaitingRawViewExpiry,
            (await db.UserErasures.FindAsync(
                [(long)erasedUser],
                cancellationToken))!.Stage);
        Assert.Equal(
            InteractionUserErasureStage.AwaitingRawViewExpiry,
            (await db.UserErasures.FindAsync(
                [(long)laterUser],
                cancellationToken))!.Stage);
        Assert.True(await db.ViewObservations.AnyAsync(cancellationToken));
        Assert.Empty(publisher.Erasures);

        db.ChangeTracker.Clear();
        var summaries = await db.EngagementSummaries
            .Where(item => resources.Select(resource => resource.Id)
                .Contains(item.ResourceId))
            .ToArrayAsync(cancellationToken);
        Assert.Equal(2, summaries.Length);
        Assert.All(summaries, summary =>
        {
            Assert.Equal(0, summary.LikeCount);
            Assert.Equal(0, summary.RatingCount);
            Assert.Equal(0, summary.RatingTotal);
        });

        clock.Advance(TimeSpan.FromDays(2));
        Assert.True(await process.Handle(
            "integration-erasure-worker",
            cancellationToken) > 0);
        Assert.True(await process.Handle(
            "integration-erasure-worker",
            cancellationToken) > 0);
        Assert.Equal(0, await process.Handle(
            "integration-erasure-worker",
            cancellationToken));
        Assert.False(await db.ViewObservations.AnyAsync(cancellationToken));
        Assert.Equal(2, publisher.Erasures.Count);
        Assert.All(publisher.Erasures, completion =>
            Assert.Equal(UserErasureOutcomes.Completed, completion.Outcome));
        Assert.Equal(2, await db.UserErasures.CountAsync(
            item => (item.UserId == erasedUser || item.UserId == laterUser)
                && item.Stage == InteractionUserErasureStage.Completed,
            cancellationToken));

        await Assert.ThrowsAsync<InteractionUnauthorizedException>(() =>
            blockedMutation.Handle(
                new SetReactionDto(resources[0], ReactionKind.Like),
                cancellationToken));
        Assert.False(await db.Reactions.AnyAsync(
            item => item.ActorId == erasedUser,
            cancellationToken));
    }

    private static UserErasureRequested CreateRequest(
        long userId,
        DateTimeOffset requestedAtUtc) => new()
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = userId,
            ErasureVersion = 1,
            RequestedAtUtc = requestedAtUtc,
        };

    private static UserErasureCompleted Completion(
        Guid eventId,
        Guid requestId,
        long userId,
        DateTimeOffset completedAtUtc) => new()
        {
            EventId = eventId,
            RequestId = requestId,
            UserId = userId,
            ErasureVersion = 1,
            Service = "interaction",
            Outcome = UserErasureOutcomes.Completed,
            CompletedAtUtc = completedAtUtc,
        };

    private static async Task ResetIsolatedDatabaseAsync(
        InteractionDbContext db,
        CancellationToken cancellationToken)
    {
        await db.ViewObservations.ExecuteDeleteAsync(cancellationToken);
        await db.Reactions.ExecuteDeleteAsync(cancellationToken);
        await db.Ratings.ExecuteDeleteAsync(cancellationToken);
        await db.ResourceStateGaps.ExecuteDeleteAsync(cancellationToken);
        await db.EngagementSummaries.ExecuteDeleteAsync(cancellationToken);
        await db.ResourceStates.ExecuteDeleteAsync(cancellationToken);
        await db.UserErasures.ExecuteDeleteAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static int NewPositiveId()
    {
        var value = BitConverter.ToInt32(Guid.NewGuid().ToByteArray())
            & int.MaxValue;
        return value == 0 ? 1 : value;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class FixedIdentity(int userId) : IIdentityService
    {
        public AuthUser User { get; } = new(userId, "integration-erasure");
    }

    private sealed class PassthroughCache : IEngagementSummaryCache
    {
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

    private sealed class CapturingPublisher : IInteractionEventPublisher
    {
        public List<UserErasureCompleted> Erasures { get; } = [];

        public Task PublishReactionAsync(
            ReactionChanged message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishRatingAsync(
            RatingChanged message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishSummaryAsync(
            EngagementSummaryChanged message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishErasureCompletedAsync(
            UserErasureCompleted message,
            CancellationToken cancellationToken)
        {
            Erasures.Add(message);
            return Task.CompletedTask;
        }
    }
}
