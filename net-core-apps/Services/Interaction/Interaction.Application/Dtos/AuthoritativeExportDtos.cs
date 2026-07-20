namespace Interaction.Application.Dtos;

public sealed record ExportVersionRangeDto(long? Minimum, long? Maximum);

public sealed record ExportPartitionChecksumDto(
    int Partition,
    int ItemCount,
    string XorSha256);

public sealed record InteractionResourceStateExportDto(
    string Lifecycle,
    string Visibility,
    long AppliedVersion,
    long HighestSeenVersion,
    bool HasGap,
    long ConcurrencyVersion,
    DateTimeOffset UpdatedAtUtc);

public sealed record InteractionEngagementSummaryExportDto(
    long LikeCount,
    long DislikeCount,
    long RatingCount,
    long RatingTotal,
    long ViewCount,
    long CommentCount,
    long ShareCount,
    long ContributionSourceVersion,
    long ContributionHighestSeenVersion,
    bool ContributionHasGap,
    bool HasExternalBaseline,
    bool IsErased,
    long Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record InteractionAuthoritativeExportItemDto(
    long Cursor,
    string ResourceType,
    string ResourceId,
    InteractionResourceStateExportDto ResourceState,
    InteractionEngagementSummaryExportDto? EngagementSummary,
    int Partition,
    string RowSha256);

public sealed record InteractionAuthoritativeExportSnapshotDto(
    long HighWatermark,
    long RevisionHighWatermark,
    DateTimeOffset CapturedAtUtc,
    long ExpectedItemCount,
    ExportVersionRangeDto ResourceVersionRange,
    ExportVersionRangeDto SummaryVersionRange,
    int PartitionCount,
    string GlobalXorSha256);

public sealed record InteractionAuthoritativeExportIntegrityDto(
    string Canonicalization,
    int ItemCount,
    ExportVersionRangeDto ResourceVersionRange,
    ExportVersionRangeDto SummaryVersionRange,
    string OrderedPageSha256,
    IReadOnlyList<ExportPartitionChecksumDto> Partitions);

public sealed record InteractionAuthoritativeExportPageDto(
    IReadOnlyList<InteractionAuthoritativeExportItemDto> Items,
    string? NextCursor,
    bool HasMore,
    InteractionAuthoritativeExportSnapshotDto Snapshot,
    InteractionAuthoritativeExportIntegrityDto Integrity);
