using Comment.Application.Features.Media.Commands;
using Comment.Application.Options;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Dtos;
using Comment.Infra.Persistence;
using Comment.Infra.Persistence.Repositories.Media;
using Comment.Infra.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using StarterEntities;
using StarterEntities.Storage;
using System.Security.Cryptography;
using Xunit;

namespace Comment.UnitTests;

[Collection(CommentMariaDbCollection.Name)]
public sealed class CommentMediaBindingMariaDbIntegrationTests
{
    private const string ConnectionEnvironment =
        "COMMENT_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Failed_binding_records_only_ingestion_recovery_and_never_flushes_dirty_business_rows()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        var cancellationToken = TestContext.Current.CancellationToken;
        var uploadIdentity = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var fingerprint = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var projectionCommentId = Random.Shared.Next(1, int.MaxValue);
        var projectionActorId = Random.Shared.Next(1, int.MaxValue);
        var ownerId = Random.Shared.Next(100_000, 1_000_000);
        var targetId = $"media-binding-rollback-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        await using (var setup = new CommentContext(options, mediator: null!))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            var ingestion = new CommentMediaIngestion(
                uploadIdentity,
                fingerprint,
                ownerId,
                "create",
                null,
                [new CommentMediaIngestionRequestItem(
                    0,
                    "image.png",
                    "image/png",
                    24,
                    MediaType.Image)],
                now);
            setup.CommentMediaIngestions.Add(ingestion);
            var projection = new CommentInteractionActorProjection(
                projectionCommentId,
                projectionActorId);
            projection.ApplyReaction(CommentReactionKind.Like, 1, now);
            setup.CommentInteractionActorProjections.Add(projection);
            await setup.SaveChangesAsync(cancellationToken);
            ingestion.MarkQuarantined(0, "quarantine-etag", now);
            ingestion.MarkClean(
                0,
                "image.png",
                "image/png",
                ".png",
                24,
                new string('a', 64),
                "ClamAV",
                "1.4.3",
                "27422",
                now,
                now);
            ingestion.MarkPromoted(
                0,
                $"{uploadIdentity}-00.png",
                "final-etag",
                TimeSpan.FromHours(24),
                now);
            await setup.SaveChangesAsync(cancellationToken);
        }

        await using (var failing = new CommentContext(options, mediator: null!))
        {
            var repository = new CommentMediaIngestionRepository(failing);
            var ingestion = await repository.FindAsync(
                uploadIdentity,
                ownerId,
                cancellationToken);
            Assert.NotNull(ingestion);

            // Deliberately leave a duplicate unrelated projection row Added. The first
            // binding SaveChanges fails, and recovery must never flush this row (or
            // the new Comment) outside the rolled-back transaction.
            var duplicateProjection = new CommentInteractionActorProjection(
                projectionCommentId,
                projectionActorId);
            duplicateProjection.ApplyReaction(CommentReactionKind.Dislike, 2, now);
            failing.CommentInteractionActorProjections.Add(duplicateProjection);
            var comment = new CommentPost(
                new User(ownerId, "Media Binding Test"),
                new AddCommentDomainDto
                {
                    Description = "must roll back",
                    TargetId = targetId,
                    TargetType = "test",
                    VisibilityLevelId = 1,
                },
                ingestion!.ToCommentMedia());
            var service = new CommentMediaIngestionService(
                repository,
                null!,
                null!,
                new CommentMediaIngestionOptions(),
                TimeProvider.System);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.BindNewCommentAsync(ingestion, comment, cancellationToken));
        }

        await using (var verify = new CommentContext(options, mediator: null!))
        {
            Assert.Equal(1, await verify.CommentInteractionActorProjections
                .AsNoTracking()
                .CountAsync(value => value.CommentId == projectionCommentId &&
                    value.ActorId == projectionActorId, cancellationToken));
            Assert.False(await verify.Comments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(value => value.TargetId == targetId, cancellationToken));
            var recovered = await verify.CommentMediaIngestions
                .Include(value => value.Items)
                .AsNoTracking()
                .SingleAsync(value => value.UploadIdentity == uploadIdentity, cancellationToken);
            Assert.Equal(CommentMediaIngestionState.Promoted, recovered.State);
            Assert.Equal(CommentMediaObjectState.Promoted, Assert.Single(recovered.Items).State);
            Assert.Equal("binding_failed", recovered.LastFailureCode);
            Assert.NotNull(recovered.NextAttemptAtUtc);

            await verify.CommentInteractionActorProjections
                .Where(value => value.CommentId == projectionCommentId &&
                    value.ActorId == projectionActorId)
                .ExecuteDeleteAsync(cancellationToken);
            await verify.CommentMediaIngestions
                .Where(value => value.UploadIdentity == uploadIdentity)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    public async Task Removing_a_comment_atomically_stages_exact_final_object_cleanup()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 11)))
            .Options;
        var cancellationToken = TestContext.Current.CancellationToken;
        var uploadIdentity = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var fingerprint = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var ownerId = Random.Shared.Next(100_000, 1_000_000);
        var targetId = $"media-delete-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var commentId = 0;

        await using (var setup = new CommentContext(options, mediator: null!))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            var ingestion = new CommentMediaIngestion(
                uploadIdentity,
                fingerprint,
                ownerId,
                "create",
                null,
                [new CommentMediaIngestionRequestItem(
                    0,
                    "image.png",
                    "image/png",
                    24,
                    MediaType.Image)],
                now);
            setup.CommentMediaIngestions.Add(ingestion);
            await setup.SaveChangesAsync(cancellationToken);
            ingestion.MarkQuarantined(0, "quarantine-etag", now);
            ingestion.MarkClean(
                0,
                "image.png",
                "image/png",
                ".png",
                24,
                new string('a', 64),
                "ClamAV",
                "1.4.3",
                "27422",
                now,
                now);
            ingestion.MarkPromoted(
                0,
                $"{uploadIdentity}-00.png",
                "final-etag",
                TimeSpan.FromHours(24),
                now);
            await setup.SaveChangesAsync(cancellationToken);

            var comment = new CommentPost(
                new User(ownerId, "Media Delete Test"),
                new AddCommentDomainDto
                {
                    Description = "delete with durable media cleanup",
                    TargetId = targetId,
                    TargetType = "test",
                    VisibilityLevelId = 1,
                },
                ingestion.ToCommentMedia());
            comment.ClearEvents();
            await new CommentMediaIngestionRepository(setup).BindNewCommentAsync(
                ingestion,
                comment,
                cancellationToken);
            commentId = comment.Id;
        }

        await using (var remove = new CommentContext(options, mediator: null!))
        {
            var repository = new CommentRepository(remove);
            var comment = await repository.GetByIdAsync(commentId, cancellationToken);
            Assert.NotNull(comment);
            comment!.PrepareForRemoval(ownerId);
            comment.ClearEvents();
            await repository.RemoveAsync(comment, cancellationToken);
        }

        await using (var verify = new CommentContext(options, mediator: null!))
        {
            Assert.False(await verify.Comments
                .IgnoreQueryFilters()
                .AnyAsync(value => value.Id == commentId, cancellationToken));
            var manifest = await verify.CommentMediaIngestions
                .Include(value => value.Items)
                .SingleAsync(value => value.UploadIdentity == uploadIdentity, cancellationToken);
            Assert.Equal(CommentMediaIngestionState.Bound, manifest.State);
            Assert.Equal(
                CommentMediaObjectState.CleanupPending,
                Assert.Single(manifest.Items).State);
            Assert.True(manifest.NextAttemptAtUtc <= DateTime.UtcNow);

            await verify.CommentMediaIngestions
                .Where(value => value.UploadIdentity == uploadIdentity)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
