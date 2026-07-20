using Comment.Application.Dtos.Media;
using Comment.Application.Options;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Comment.Application.Features.Privacy;

namespace Comment.Application.Features.Media.Commands;

public sealed class CommentMediaReconciliationCycle(
    ICommentMediaIngestionRepository repository,
    CommentMediaIngestionService ingestionService,
    CommentMediaIngestionOptions options,
    TimeProvider timeProvider,
    ICommentUserErasureAuthority erasure)
{
    public async Task<CommentMediaReconciliationResult> ExecuteAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var claimed = await repository.ClaimDueAsync(
            workerId,
            now,
            options.ReconciliationBatchSize,
            options.LeaseDuration,
            cancellationToken);
        var completed = 0;
        var retried = 0;
        var compensated = 0;

        foreach (var ingestion in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var mutation = await erasure.AcquireMutationAsync(
                [ingestion.OwnerUserId], cancellationToken);
            if (!mutation.Allowed)
            {
                await mutation.CommitAsync(cancellationToken);
                continue;
            }
            try
            {
                var lastPromotionAtUtc = ingestion.Items
                    .Select(item => item.PromotedAtUtc)
                    .Where(timestamp => timestamp.HasValue)
                    .Select(timestamp => timestamp.GetValueOrDefault())
                    .DefaultIfEmpty(DateTime.MaxValue)
                    .Max();
                if (ingestion.State == CommentMediaIngestionState.Promoted
                    && now - lastPromotionAtUtc >= options.PromotedOrphanRetention)
                {
                    ingestion.MarkOrphaned(now);
                    await repository.SaveAsync(ingestion, cancellationToken);
                }

                if (ingestion.HasPendingFinalObjectCleanup)
                {
                    await ingestionService.CleanupDeletedFinalObjectsAsync(
                        ingestion,
                        cancellationToken);
                    completed++;
                }
                else if (ingestion.State == CommentMediaIngestionState.CompensationPending)
                {
                    await ingestionService.CompensateAsync(ingestion, cancellationToken);
                    compensated++;
                }
                else
                {
                    await ingestionService.ResumeAsync(ingestion, cancellationToken);
                    if (ingestion.State == CommentMediaIngestionState.Promoted)
                        completed++;
                    else
                        retried++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                retried++;
            }
            // Resume/compensation deliberately records retry state before
            // surfacing storage/scanner faults. Preserve either success or
            // retry state in the same owner-guarded transaction; an aborted DB
            // transaction still fails this commit and escapes the cycle.
            await mutation.CommitAsync(cancellationToken);
        }

        return new(claimed.Count, completed, retried, compensated);
    }

    public Task<int> PurgeAsync(CancellationToken cancellationToken) =>
        repository.PurgeTerminalAsync(
            timeProvider.GetUtcNow().UtcDateTime.Subtract(options.TerminalRetention),
            options.ReconciliationBatchSize,
            cancellationToken);
}
