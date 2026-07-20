using Comment.Application.Features.Visibility;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Visibility;
using Comment.Domain.Dtos;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Visibility;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StarterEntities;
using StarterEntities.Storage;
using Xunit;

namespace Comment.UnitTests;

[Collection(CommentMariaDbCollection.Name)]
public sealed class CommentAudienceMariaDbIntegrationTests
{
    private const string ConnectionEnvironment = "COMMENT_MARIADB_TEST_CONNECTION";
    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Privacy_matrix_is_local_bounded_and_fails_closed_for_blocks_revokes_and_gaps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 11))).Options;
        await using var context = new CommentContext(options, mediator: null!);
        await context.Database.MigrateAsync(cancellationToken);
        await context.CommentRelationshipEventQuarantines.ExecuteDeleteAsync(cancellationToken);
        await context.CommentRelationshipEdges.ExecuteDeleteAsync(cancellationToken);
        await context.CommentRelationshipCheckpoints.ExecuteDeleteAsync(cancellationToken);
        await context.CommentAudienceProjectionStates.ExecuteDeleteAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var bootstrap = CommentAudienceProjectionState.Create(now);
        var lease = bootstrap.TryLease("privacy-test", now, TimeSpan.FromMinutes(1));
        bootstrap.BeginCycle(lease!.Value, now);
        bootstrap.ApplyPage(lease.Value, 0, DateTime.UnixEpoch, 0, false, now);
        context.CommentAudienceProjectionStates.Add(bootstrap);
        await context.SaveChangesAsync(cancellationToken);

        var telemetry = new CommentAudienceProjectionTelemetry();
        var projectionOptions = Options.Create(new CommentAudienceProjectionOptions
        {
            EnableReadEnforcement = true,
            MaximumCheckpointAgeSeconds = 3600,
            MaximumBootstrapAgeSeconds = 3600,
            MaximumTwoHopFirstHop = 16,
        });
        var writer = new CommentRelationshipProjectionStore(
            context,
            telemetry,
            AllowAllCommentUserErasureAuthority.Instance);
        var policy = new CommentAudienceSqlPolicy(context, projectionOptions, telemetry,
            TimeProvider.System);

        var author = Random.Shared.Next(2_000_000, 3_000_000);
        var intermediate = author + 1;
        var viewer = author + 2;
        var firstFriend = Event(author, intermediate, "friend", "active", 1);
        var secondFriend = Event(intermediate, viewer, "friend", "active", 1);
        await writer.ApplyAsync(firstFriend, now, cancellationToken);
        await writer.ApplyAsync(secondFriend, now, cancellationToken);

        Assert.True((await policy.EvaluateAudienceAsync(author, "friends_of_friends", viewer,
            cancellationToken)).Allowed);
        Assert.False((await policy.EvaluateAudienceAsync(author, "friends", viewer,
            cancellationToken)).Allowed);
        Assert.True((await policy.EvaluateAudienceAsync(author, "public", null,
            cancellationToken)).Allowed);
        Assert.False((await policy.EvaluateAudienceAsync(author, "friends", null,
            cancellationToken)).Allowed);

        var fofComment = CreateComment(author, 3);
        var publicComment = CreateComment(author, 1);
        var ownerComment = CreateComment(author, 4);
        context.Comments.AddRange(fofComment, publicComment, ownerComment);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        var visibleToViewer = await (await policy.ApplyReadFilterAsync(context.Comments, viewer,
                cancellationToken)).Where(item => item.Id == fofComment.Id).AnyAsync(cancellationToken);
        Assert.True(visibleToViewer);
        var anonymousIds = await (await policy.ApplyReadFilterAsync(context.Comments, null,
                cancellationToken)).Where(item => item.AuthorUserId == author)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        Assert.Contains(publicComment.Id, anonymousIds);
        Assert.DoesNotContain(fofComment.Id, anonymousIds);
        Assert.DoesNotContain(ownerComment.Id, anonymousIds);

        var block = Event(viewer, author, "block", "blocked", 1);
        await writer.ApplyAsync(block, now.AddSeconds(1), cancellationToken);
        Assert.False((await policy.EvaluateAudienceAsync(author, "public", viewer,
            cancellationToken)).Allowed);
        await writer.ApplyAsync(block with
        {
            EventId = Guid.NewGuid(),
            StreamVersion = 2,
            State = "revoked",
            OccurredAtUtc = now.AddSeconds(2),
        }, now.AddSeconds(2), cancellationToken);
        Assert.True((await policy.EvaluateAudienceAsync(author, "public", viewer,
            cancellationToken)).Allowed);

        await writer.ApplyAsync(secondFriend with
        {
            EventId = Guid.NewGuid(),
            StreamVersion = 2,
            State = "revoked",
            OccurredAtUtc = now.AddSeconds(3),
        }, now.AddSeconds(3), cancellationToken);
        Assert.False((await policy.EvaluateAudienceAsync(author, "friends_of_friends", viewer,
            cancellationToken)).Allowed);

        var gap = Event(author, viewer, "friend", "active", 3);
        var result = await writer.ApplyAsync(gap, now.AddSeconds(4), cancellationToken);
        Assert.Equal(CommentRelationshipProjectionOutcome.GapQuarantined, result.Outcome);
        Assert.Equal(CommentAudienceOutcome.ProjectionUnavailable,
            (await policy.EvaluateAudienceAsync(author, "public", viewer, cancellationToken)).Outcome);
        Assert.True((await policy.EvaluateAudienceAsync(author, "public", null,
            cancellationToken)).Allowed);
    }

    private static RelationshipStateChanged Event(int owner, int viewer, string kind,
        string state, long version) => new()
        {
            EventId = Guid.NewGuid(),
            StreamId = Guid.NewGuid(),
            StreamVersion = version,
            OwnerUserId = owner,
            ViewerUserId = viewer,
            RelationshipKind = kind,
            State = state,
            OccurredAtUtc = DateTime.UtcNow,
            ReasonCode = "test",
        };

    private static CommentPost CreateComment(int author, int visibility) => new(
        new User(author, $"author-{author}"),
        new AddCommentDomainDto
        {
            Description = "privacy matrix",
            TargetId = Guid.NewGuid().ToString("N"),
            TargetType = "post",
            VisibilityLevelId = visibility,
        },
        Array.Empty<NewCommentMedia>());
}
