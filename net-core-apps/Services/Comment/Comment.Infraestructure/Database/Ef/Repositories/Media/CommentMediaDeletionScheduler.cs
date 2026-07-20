using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Repositories.Media;

/// <summary>
/// Stages exact final-object deletion on the existing durable ingestion
/// manifest. Callers persist this state in the same transaction that removes
/// the corresponding CommentMediaItem row.
/// </summary>
internal static class CommentMediaDeletionScheduler
{
    public static async Task StageAsync(
        CommentContext context,
        CommentPost comment,
        IEnumerable<CommentMediaItem> mediaItems,
        CancellationToken cancellationToken)
    {
        var requested = (mediaItems ?? Array.Empty<CommentMediaItem>())
            .Where(item => item is not null)
            .Select(item => new MediaReference(item.Id, item.Url?.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.ObjectReference))
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
            return;

        var manifests = await context.CommentMediaIngestions
            .Include(value => value.Items)
            .Where(value => value.BoundCommentId == comment.Id)
            .ToListAsync(cancellationToken);
        var scheduled = new Dictionary<CommentMediaIngestion, List<int>>();

        foreach (var media in requested)
        {
            var matches = manifests
                .SelectMany(manifest => manifest.Items.Select(item => new
                {
                    Manifest = manifest,
                    Item = item,
                }))
                .Where(candidate =>
                    candidate.Manifest.State == CommentMediaIngestionState.Bound
                    && candidate.Item.State == CommentMediaObjectState.Bound
                    && string.Equals(
                        candidate.Item.PrivateObjectUrl,
                        media.ObjectReference,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new CommentException(
                    $"Media item '{media.MediaItemId}' has no unique private-object ownership manifest.",
                    503);
            }

            if (!scheduled.TryGetValue(matches[0].Manifest, out var orders))
            {
                orders = [];
                scheduled.Add(matches[0].Manifest, orders);
            }
            orders.Add(matches[0].Item.Order);
        }

        var now = DateTime.UtcNow;
        foreach (var work in scheduled)
            work.Key.ScheduleBoundObjectCleanup(work.Value, now);
    }

    private sealed record MediaReference(int MediaItemId, string? ObjectReference);
}
