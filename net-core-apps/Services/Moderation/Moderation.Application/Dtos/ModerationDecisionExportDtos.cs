namespace Moderation.Application.Dtos;

public sealed record ModerationExportVersionRangeDto(long? Minimum, long? Maximum);

public sealed record ModerationExportPartitionChecksumDto(
    int Partition,
    int ItemCount,
    string XorSha256);

public sealed record ModerationDecisionExportItemDto(
    long Cursor,
    Guid CaseId,
    string ResourceKind,
    string ResourceId,
    string Action,
    long DecisionVersion,
    long CaseVersion,
    DateTimeOffset DecidedAtUtc,
    int Partition,
    string RowSha256);

public sealed record ModerationDecisionExportSnapshotDto(
    long HighWatermark,
    long RevisionHighWatermark,
    DateTimeOffset CapturedAtUtc,
    long ExpectedItemCount,
    ModerationExportVersionRangeDto DecisionVersionRange,
    ModerationExportVersionRangeDto CaseVersionRange,
    int PartitionCount,
    string GlobalXorSha256);

public sealed record ModerationDecisionExportIntegrityDto(
    string Canonicalization,
    int ItemCount,
    ModerationExportVersionRangeDto DecisionVersionRange,
    ModerationExportVersionRangeDto CaseVersionRange,
    string OrderedPageSha256,
    IReadOnlyList<ModerationExportPartitionChecksumDto> Partitions);

public sealed record ModerationDecisionExportPageDto(
    IReadOnlyList<ModerationDecisionExportItemDto> Items,
    string? NextCursor,
    bool HasMore,
    ModerationDecisionExportSnapshotDto Snapshot,
    ModerationDecisionExportIntegrityDto Integrity);
