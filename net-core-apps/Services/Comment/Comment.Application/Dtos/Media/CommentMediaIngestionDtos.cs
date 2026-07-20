using Comment.Domain.AggregatesModel.Media;
using System.Collections.Generic;

namespace Comment.Application.Dtos.Media;

public sealed record CommentMediaIngestionResult(
    CommentMediaIngestion Ingestion,
    IReadOnlyList<Comment.Domain.AggregatesModel.NewCommentMedia> Media,
    bool IsReplay);

public sealed record CommentMediaReconciliationResult(
    int Claimed,
    int Completed,
    int Retried,
    int Compensated);
