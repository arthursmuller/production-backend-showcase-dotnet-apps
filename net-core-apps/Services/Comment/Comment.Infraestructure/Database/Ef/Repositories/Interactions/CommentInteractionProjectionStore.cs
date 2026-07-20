using Comment.Application.Features.Interactions;
using Comment.Application.Providers.Interactions;
using Comment.Application.Dtos.Interactions;
using Comment.Infra.Persistence;
using Interaction.Contracts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Comment.Domain.AggregatesModel.Interactions;
using Comment.Application.Features.Privacy;

namespace Comment.Infra.Persistence.Repositories.Interactions
{
    public sealed class CommentInteractionProjectionStore(
        CommentContext context,
        ICommentUserErasureAuthority erasure)
        : ICommentInteractionProjectionStore
    {
        public async Task ApplySummaryAsync(
            EngagementSummaryChanged message,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(message.ResourceType, "comment", StringComparison.OrdinalIgnoreCase))
                return;
            if (!int.TryParse(message.ResourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var commentId) || commentId <= 0)
                throw new InvalidOperationException("Comment summary resource id must be a positive integer.");
            var ownerId = await context.Comments.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.Id == commentId)
                .Select(value => (int?)value.AuthorUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (!ownerId.HasValue)
                return;
            await using var mutation = await erasure.AcquireMutationAsync(
                [ownerId.Value], cancellationToken);
            if (!mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                return;
            }
            if (!await context.Comments.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(value => value.Id == commentId, cancellationToken))
            {
                await mutation.CommitAsync(cancellationToken);
                return;
            }
            var projection = await context.Set<CommentInteractionSummaryProjection>()
                .SingleOrDefaultAsync(x => x.CommentId == commentId, cancellationToken);
            var snapshot = ToSnapshot(message);
            if (projection is null)
                context.Add(new CommentInteractionSummaryProjection(commentId, snapshot));
            else
            {
                var appliedBefore = projection.AppliedVersion;
                projection.Apply(snapshot);
                if (message.SummaryVersion > appliedBefore + 1)
                {
                    var exists = await context.Set<CommentInteractionSummaryGap>()
                        .AnyAsync(
                            x => x.CommentId == commentId &&
                                x.SummaryVersion == message.SummaryVersion,
                            cancellationToken);
                    if (!exists)
                        context.Add(new CommentInteractionSummaryGap(commentId, snapshot));
                }
                else if (message.SummaryVersion == appliedBefore + 1)
                {
                    for (var drained = 0; drained < 256; drained++)
                    {
                        var gap = await context.Set<CommentInteractionSummaryGap>()
                            .SingleOrDefaultAsync(
                                x => x.CommentId == commentId &&
                                    x.SummaryVersion == projection.AppliedVersion + 1,
                                cancellationToken);
                        if (gap is null)
                            break;
                        projection.Apply(gap.ToSnapshot());
                        context.Remove(gap);
                    }
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
        }

        public Task ApplyReactionAsync(
            ReactionChanged message,
            CancellationToken cancellationToken) => ApplyActorAsync(
                message.ResourceType,
                message.ResourceId,
                message.ActorId,
                message.InteractionVersion,
                projection => projection.ApplyReaction(
                    message.Kind.HasValue
                        ? (CommentReactionKind?)(byte)message.Kind.Value
                        : null,
                    message.InteractionVersion,
                    message.OccurredAtUtc),
                cancellationToken);

        public Task ApplyRatingAsync(
            RatingChanged message,
            CancellationToken cancellationToken) => ApplyActorAsync(
                message.ResourceType,
                message.ResourceId,
                message.ActorId,
                message.InteractionVersion,
                projection => projection.ApplyRating(
                    message.Score,
                    message.InteractionVersion,
                    message.OccurredAtUtc),
                cancellationToken);

        public async Task<IReadOnlyDictionary<int, CommentInteractionSummary>> GetSummariesAsync(
            IReadOnlyCollection<int> commentIds,
            CancellationToken cancellationToken)
        {
            var ids = NormalizeIds(commentIds);
            if (ids.Length == 0)
                return new Dictionary<int, CommentInteractionSummary>();
            var rows = await context.Set<CommentInteractionSummaryProjection>()
                .AsNoTracking()
                .Where(projection => ids.Contains(projection.CommentId))
                .ToArrayAsync(cancellationToken);
            return rows.ToDictionary(
                projection => projection.CommentId,
                ToSummary);
        }

        private static int[] NormalizeIds(IReadOnlyCollection<int> commentIds)
        {
            if (commentIds is null)
                return Array.Empty<int>();
            var ids = commentIds.Distinct().ToArray();
            if (ids.Length > 100 || ids.Any(id => id <= 0))
                throw new ArgumentOutOfRangeException(
                    nameof(commentIds),
                    "A bounded set of at most 100 positive comment IDs is required.");
            return ids;
        }

        private static CommentInteractionSummary ToSummary(
            CommentInteractionSummaryProjection projection) => new(
                projection.LikeCount,
                projection.DislikeCount,
                projection.RatingCount,
                projection.RatingTotal,
                projection.ViewCount,
                projection.CommentCount,
                projection.ShareCount,
                projection.AppliedVersion,
                projection.HasGap);

        private static CommentInteractionSummarySnapshot ToSnapshot(
            EngagementSummaryChanged message) => new(
                message.LikeCount,
                message.DislikeCount,
                message.RatingCount,
                message.RatingTotal,
                message.ViewCount,
                message.CommentCount,
                message.ShareCount,
                message.SummaryVersion,
                message.OccurredAtUtc);

        private async Task ApplyActorAsync(
            string resourceType,
            string resourceId,
            long actorId,
            long version,
            Func<CommentInteractionActorProjection, bool> apply,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(resourceType, "comment", StringComparison.OrdinalIgnoreCase))
                return;
            if (actorId <= 0 || version <= 0 ||
                !int.TryParse(resourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var commentId) ||
                commentId <= 0)
                throw new InvalidOperationException("Interaction actor event coordinates are invalid.");
            var ownerId = await context.Comments.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.Id == commentId)
                .Select(value => (int?)value.AuthorUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (!ownerId.HasValue)
                return;
            await using var mutation = await erasure.AcquireMutationAsync(
                [checked((int)actorId), ownerId.Value], cancellationToken);
            if (!mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                return;
            }
            if (!await context.Comments.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(value => value.Id == commentId, cancellationToken))
            {
                await mutation.CommitAsync(cancellationToken);
                return;
            }
            var projection = await context.Set<CommentInteractionActorProjection>()
                .SingleOrDefaultAsync(
                    x => x.CommentId == commentId && x.ActorId == actorId,
                    cancellationToken);
            if (projection is null)
            {
                projection = new CommentInteractionActorProjection(commentId, actorId);
                context.Add(projection);
            }
            if (apply(projection))
                await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
        }
    }
}
