using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Media;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Domain.Repositories;

public sealed record CommentMediaIngestionGetResult(
    CommentMediaIngestion Ingestion,
    bool Created);

public interface ICommentMediaIngestionRepository
{
    Task<CommentMediaIngestionGetResult> GetOrCreateAsync(
        CommentMediaIngestion candidate,
        CancellationToken cancellationToken);

    Task<CommentMediaIngestion?> FindAsync(
        string uploadIdentity,
        int ownerUserId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken);

    Task<bool> TryClaimForRequestAsync(
        CommentMediaIngestion ingestion,
        string leaseOwner,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RecordBindingFailureAsync(
        string uploadIdentity,
        int ownerUserId,
        DateTime utcNow,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken);

    Task<CommentPost> BindNewCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken);

    Task<CommentPost> BindUpdatedCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CommentMediaIngestion>> ClaimDueAsync(
        string leaseOwner,
        DateTime utcNow,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<int> PurgeTerminalAsync(
        DateTime completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken);
}
