using Comment.API.AppInterfaces.Queues;
using Comment.Application.Features.Interactions;
using Comment.Application.Features.Moderation.Commands;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Comment.Domain.AggregatesModel.Moderation;
using Comment.Domain.Dtos;
using Comment.Domain.Repositories;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Repositories;
using Comment.Infra.Persistence.Repositories.Moderation;
using Microsoft.EntityFrameworkCore;
using Moderation.Contracts;
using StarterEntities;
using Xunit;

namespace Comment.UnitTests;

[Collection(CommentMariaDbCollection.Name)]
public sealed class CommentModerationDecisionProjectionMariaDbIntegrationTests
{
    private const string ConnectionEnvironment = "COMMENT_MARIADB_TEST_CONNECTION";
    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Both_contracts_converge_and_suppression_is_exact_monotonic_and_atomic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        await using var context = new CommentContext(options, mediator: null!);
        await context.Database.MigrateAsync(cancellationToken);

        var now = new DateTimeOffset(2026, 7, 17, 22, 0, 0, TimeSpan.Zero);
        var ownerId = Random.Shared.Next(1_000_000, 1_500_000);
        var actorId = ownerId + 1;
        var targetId = $"moderation-{Guid.NewGuid():N}";
        var comment = CreateComment(ownerId, targetId);
        context.Comments.Add(comment);
        await context.SaveChangesAsync(cancellationToken);
        var actor = new CommentInteractionActorProjection(comment.Id, actorId);
        actor.ApplyReaction(CommentReactionKind.Like, 1, now);
        context.CommentInteractionActorProjections.Add(actor);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var projectionRepository = new CommentModerationDecisionRepository(context);
        var command = new ApplyCommentModerationDecisionCommand(
            projectionRepository,
            new FixedTimeProvider(now));
        var decisionConsumer = new ModerationDecisionMadeConsumer(command);
        var actionConsumer = new ModerationActionAppliedConsumer(command);
        var caseId = Guid.NewGuid();
        var first = new ModerationDecisionMade(
            caseId,
            ModeratedResourceKind.Comment,
            comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ModerationDecisionAction.RemoveContent,
            1,
            now.AddMinutes(-3));

        await decisionConsumer.ConsumeMessageAsync(first, cancellationToken);
        await actionConsumer.ConsumeMessageAsync(
            new ModerationActionApplied(
                first.CaseId,
                first.ResourceKind,
                first.ResourceId,
                first.Action,
                first.DecisionVersion,
                first.OccurredAtUtc),
            cancellationToken);

        var projected = await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .SingleAsync(value => value.CaseId == caseId, cancellationToken);
        Assert.Equal(1, projected.AppliedVersion);
        Assert.True(projected.IsSuppressed);
        Assert.Empty(await context.CommentModerationDecisionGaps
            .Where(value => value.CaseId == caseId)
            .ToArrayAsync(cancellationToken));

        await AssertSuppressedAcrossReadsAsync(
            context, comment.Id, ownerId, actorId, targetId, cancellationToken);

        await decisionConsumer.ConsumeMessageAsync(first with
        {
            Action = ModerationDecisionAction.NoAction,
            DecisionVersion = 3,
            OccurredAtUtc = now.AddMinutes(-1),
        }, cancellationToken);
        projected = await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .SingleAsync(value => value.CaseId == caseId, cancellationToken);
        Assert.Equal(1, projected.AppliedVersion);
        Assert.Equal(3, projected.HighestSeenVersion);
        Assert.True(projected.HasGap);
        Assert.True(projected.IsSuppressed);
        Assert.Single(await context.CommentModerationDecisionGaps
            .Where(value => value.CaseId == caseId)
            .ToArrayAsync(cancellationToken));

        await actionConsumer.ConsumeMessageAsync(
            new ModerationActionApplied(
                caseId,
                ModeratedResourceKind.Comment,
                first.ResourceId,
                ModerationDecisionAction.NoAction,
                2,
                now.AddMinutes(-2)),
            cancellationToken);
        context.ChangeTracker.Clear();
        projected = await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .SingleAsync(value => value.CaseId == caseId, cancellationToken);
        Assert.Equal(3, projected.AppliedVersion);
        Assert.False(projected.HasGap);
        Assert.False(projected.IsSuppressed);
        Assert.Empty(await context.CommentModerationDecisionGaps
            .Where(value => value.CaseId == caseId)
            .ToArrayAsync(cancellationToken));

        await decisionConsumer.ConsumeMessageAsync(first with
        {
            DecisionVersion = 2,
            OccurredAtUtc = now.AddMinutes(-2),
        }, cancellationToken);
        Assert.False((await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .SingleAsync(value => value.CaseId == caseId, cancellationToken)).IsSuppressed);
        await AssertReadableAcrossReadsAsync(
            context, comment.Id, ownerId, actorId, targetId, cancellationToken);

        var wrongKindCase = Guid.NewGuid();
        await decisionConsumer.ConsumeMessageAsync(
            new ModerationDecisionMade(
                wrongKindCase,
                ModeratedResourceKind.Post,
                comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ModerationDecisionAction.RemoveContent,
                1,
                now),
            cancellationToken);
        Assert.False(await context.CommentModerationDecisionProjections
            .AnyAsync(value => value.CaseId == wrongKindCase, cancellationToken));

        var boundedCase = Guid.NewGuid();
        await decisionConsumer.ConsumeMessageAsync(first with
        {
            CaseId = boundedCase,
            Action = ModerationDecisionAction.NoAction,
            DecisionVersion = 1,
            OccurredAtUtc = now.AddMinutes(-4),
        }, cancellationToken);
        for (var version = 3L;
             version <= CommentModerationDecisionRepository.MaximumBufferedGapsPerCase + 3L;
             version++)
        {
            await decisionConsumer.ConsumeMessageAsync(first with
            {
                CaseId = boundedCase,
                Action = ModerationDecisionAction.NoAction,
                DecisionVersion = version,
                OccurredAtUtc = now.AddMinutes(-1),
            }, cancellationToken);
        }
        Assert.Equal(
            CommentModerationDecisionRepository.MaximumBufferedGapsPerCase,
            await context.CommentModerationDecisionGaps.CountAsync(
                value => value.CaseId == boundedCase,
                cancellationToken));
        Assert.True((await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .SingleAsync(value => value.CaseId == boundedCase, cancellationToken)).IsSuppressed);

        var rolledBackCase = Guid.NewGuid();
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await actionConsumer.ConsumeMessageAsync(
                new ModerationActionApplied(
                    rolledBackCase,
                    ModeratedResourceKind.Comment,
                    first.ResourceId,
                    ModerationDecisionAction.RemoveContent,
                    1,
                    now),
                cancellationToken);
            Assert.True(await context.CommentModerationDecisionProjections
                .AnyAsync(value => value.CaseId == rolledBackCase, cancellationToken));
            await transaction.RollbackAsync(cancellationToken);
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.CommentModerationDecisionProjections
            .AsNoTracking()
            .AnyAsync(value => value.CaseId == rolledBackCase, cancellationToken));
    }

    private static async Task AssertSuppressedAcrossReadsAsync(
        CommentContext context,
        int commentId,
        int ownerId,
        int actorId,
        string targetId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var reads = ReadRepository(context);
        var criteria = Criteria(targetId);
        Assert.Null(await reads.GetByIdAsync(commentId, cancellationToken));
        Assert.Empty((await reads.ListCursorAsync(
            criteria, null!, 10, cancellationToken)).Items);
        Assert.Empty((await reads.ListOwnedCursorAsync(
            criteria, null!, 10, ownerId, cancellationToken)).Items);
        Assert.Empty(await reads.GetInteractionStatusesAsync(
            [commentId], actorId, cancellationToken));
    }

    private static async Task AssertReadableAcrossReadsAsync(
        CommentContext context,
        int commentId,
        int ownerId,
        int actorId,
        string targetId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var reads = ReadRepository(context);
        var criteria = Criteria(targetId);
        Assert.NotNull(await reads.GetByIdAsync(commentId, cancellationToken));
        Assert.Contains((await reads.ListCursorAsync(
            criteria, null!, 10, cancellationToken)).Items,
            value => value.Id == commentId);
        Assert.Contains((await reads.ListOwnedCursorAsync(
            criteria, null!, 10, ownerId, cancellationToken)).Items,
            value => value.Id == commentId);
        Assert.True(Assert.Single(await reads.GetInteractionStatusesAsync(
            [commentId], actorId, cancellationToken)).Liked);
    }

    private static CommentReadRepository ReadRepository(CommentContext context) => new(context);

    private static CommentQueryCriteria Criteria(string targetId) => new()
    {
        Targets = [new CommentTarget(targetId, "post")],
        PageSize = 10,
    };

    private static CommentPost CreateComment(int ownerId, string targetId) => new(
        new User(ownerId, $"moderation-owner-{ownerId}"),
        new AddCommentDomainDto
        {
            Description = "moderation projection integration test",
            TargetId = targetId,
            TargetType = "post",
            VisibilityLevelId = 1,
        },
        Array.Empty<NewCommentMedia>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
