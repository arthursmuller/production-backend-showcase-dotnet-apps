using Interaction.Domain.AggregatesModel;

namespace Interaction.Domain.Repositories;

public sealed record InteractionAuthoritativeExportSnapshot(
    long HighWatermark,
    long RevisionHighWatermark,
    DateTimeOffset CapturedAtUtc,
    long ExpectedItemCount,
    long? MinimumResourceVersion,
    long? MaximumResourceVersion,
    long? MinimumSummaryVersion,
    long? MaximumSummaryVersion,
    string GlobalXorSha256);

public sealed record InteractionAuthoritativeExportPartition(
    int Partition,
    long ItemCount,
    string XorSha256);

public sealed record InteractionAuthoritativeExportRow(
    long Cursor,
    ResourceType ResourceType,
    string ResourceId,
    ResourceLifecycle Lifecycle,
    ResourceVisibility Visibility,
    long AppliedResourceVersion,
    long HighestSeenResourceVersion,
    bool ResourceHasGap,
    long ResourceConcurrencyVersion,
    DateTimeOffset ResourceUpdatedAtUtc,
    long? LikeCount,
    long? DislikeCount,
    long? RatingCount,
    long? RatingTotal,
    long? ViewCount,
    long? CommentCount,
    long? ShareCount,
    long? ContributionSourceVersion,
    long? ContributionHighestSeenVersion,
    bool? ContributionHasGap,
    bool? HasExternalBaseline,
    bool? IsErased,
    long? SummaryVersion,
    DateTimeOffset? SummaryUpdatedAtUtc,
    byte PartitionKey,
    string RowSha256);

public sealed record InteractionAuthoritativeExportSlice(
    IReadOnlyList<InteractionAuthoritativeExportRow> Items,
    bool HasMore);

public interface IInteractionAuthoritativeExportRepository
{
    Task<InteractionAuthoritativeExportSnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken);

    Task<InteractionAuthoritativeExportSlice> ReadPageAsync(
        long afterCursor,
        long snapshotHighWatermark,
        long snapshotRevisionHighWatermark,
        int take,
        CancellationToken cancellationToken);
}
