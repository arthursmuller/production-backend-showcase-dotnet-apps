using Moderation.Domain.AggregatesModel;

namespace Moderation.Domain.Repositories;

public sealed record ModerationDecisionExportSnapshot(
    long HighWatermark,
    long RevisionHighWatermark,
    DateTimeOffset CapturedAtUtc,
    long ExpectedItemCount,
    long? MinimumDecisionVersion,
    long? MaximumDecisionVersion,
    long? MinimumCaseVersion,
    long? MaximumCaseVersion,
    string GlobalXorSha256);

public sealed record ModerationDecisionExportPartition(
    int Partition,
    long ItemCount,
    string XorSha256);

public sealed record ModerationDecisionExportRow(
    long Cursor,
    Guid CaseId,
    ResourceKind ResourceKind,
    string ResourceId,
    DecisionAction Action,
    long DecisionVersion,
    long CaseVersion,
    DateTimeOffset DecidedAtUtc,
    byte PartitionKey,
    string RowSha256);

public sealed record ModerationDecisionExportSlice(
    IReadOnlyList<ModerationDecisionExportRow> Items,
    bool HasMore);

public interface IModerationDecisionExportRepository
{
    Task<ModerationDecisionExportSnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken);

    Task<ModerationDecisionExportSlice> ReadPageAsync(
        long afterCursor,
        long snapshotHighWatermark,
        long snapshotRevisionHighWatermark,
        int take,
        CancellationToken cancellationToken);
}
