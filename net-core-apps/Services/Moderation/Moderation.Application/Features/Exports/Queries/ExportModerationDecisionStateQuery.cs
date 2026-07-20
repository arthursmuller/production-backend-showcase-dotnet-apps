using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Application.Options;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Exports.Queries;

public sealed class ExportModerationDecisionStateQuery(
    IModerationDecisionExportRepository repository,
    ModerationDecisionExportCursor cursorCodec,
    TimeProvider timeProvider)
{
    private const int PartitionCount = 256;
    private const string Canonicalization = "moderation-decision-authority/sha256";

    public async Task<ModerationResult<ModerationDecisionExportPageDto>> HandleAsync(
        string? cursor,
        int? requestedPageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (afterCursor, pageSize, snapshot, issuedAtUtc) = await ResolveRequestAsync(
                cursor,
                requestedPageSize,
                cancellationToken);
            var slice = await repository.ReadPageAsync(
                afterCursor,
                snapshot.HighWatermark,
                snapshot.RevisionHighWatermark,
                pageSize,
                cancellationToken);
            var materialized = slice.Items.Select(ToHashedItem).ToArray();
            var nextCursor = slice.HasMore && slice.Items.Count > 0
                ? cursorCodec.Encode(new ModerationDecisionExportCursorState(
                    slice.Items[^1].Cursor,
                    pageSize,
                    snapshot,
                    issuedAtUtc))
                : null;
            return ModerationResult<ModerationDecisionExportPageDto>.Success(
                new ModerationDecisionExportPageDto(
                    materialized.Select(item => item.Item).ToArray(),
                    nextCursor,
                    slice.HasMore,
                    new ModerationDecisionExportSnapshotDto(
                        snapshot.HighWatermark,
                        snapshot.RevisionHighWatermark,
                        snapshot.CapturedAtUtc,
                        snapshot.ExpectedItemCount,
                        new ModerationExportVersionRangeDto(
                            snapshot.MinimumDecisionVersion,
                            snapshot.MaximumDecisionVersion),
                        new ModerationExportVersionRangeDto(
                            snapshot.MinimumCaseVersion,
                            snapshot.MaximumCaseVersion),
                        PartitionCount,
                        snapshot.GlobalXorSha256),
                    Integrity(materialized)));
        }
        catch (ModerationValidationException exception)
        {
            return ModerationFeatureSupport.Validation<ModerationDecisionExportPageDto>(
                exception.Message);
        }
    }

    private async Task<(
        long After,
        int PageSize,
        ModerationDecisionExportSnapshot Snapshot,
        DateTimeOffset IssuedAtUtc)>
        ResolveRequestAsync(
            string? cursor,
            int? requestedPageSize,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            var pageSize = requestedPageSize ?? ModerationDecisionExportOptions.MaximumPageSize;
            ValidatePageSize(pageSize);
            return (
                0,
                pageSize,
                await repository.CaptureSnapshotAsync(cancellationToken),
                timeProvider.GetUtcNow());
        }

        var decoded = cursorCodec.Decode(cursor);
        if (requestedPageSize.HasValue && requestedPageSize.Value != decoded.PageSize)
        {
            throw new ModerationValidationException(
                "A continuation request must retain the first page's page size.");
        }
        return (decoded.AfterCursor, decoded.PageSize, decoded.Snapshot, decoded.IssuedAtUtc);
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > ModerationDecisionExportOptions.MaximumPageSize)
        {
            throw new ModerationValidationException(
                $"Decision export page size must be between 1 and {ModerationDecisionExportOptions.MaximumPageSize}.");
        }
    }

    private static HashedItem ToHashedItem(ModerationDecisionExportRow row)
    {
        var hash = Convert.FromHexString(row.RowSha256);
        var item = new ModerationDecisionExportItemDto(
            row.Cursor,
            row.CaseId,
            row.ResourceKind.ToString().ToLowerInvariant(),
            row.ResourceId,
            row.Action.ToString().ToLowerInvariant(),
            row.DecisionVersion,
            row.CaseVersion,
            row.DecidedAtUtc,
            row.PartitionKey,
            row.RowSha256);
        return new HashedItem(item, hash, row.DecisionVersion, row.CaseVersion);
    }

    private static ModerationDecisionExportIntegrityDto Integrity(
        IReadOnlyList<HashedItem> items)
    {
        using var pageHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in items)
            pageHash.AppendData(item.Hash);
        var partitions = items
            .GroupBy(item => item.Item.Partition)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var xor = new byte[32];
                foreach (var item in group)
                    for (var index = 0; index < xor.Length; index++)
                        xor[index] ^= item.Hash[index];
                return new ModerationExportPartitionChecksumDto(
                    group.Key,
                    group.Count(),
                    Convert.ToHexStringLower(xor));
            })
            .ToArray();
        return new ModerationDecisionExportIntegrityDto(
            Canonicalization,
            items.Count,
            new ModerationExportVersionRangeDto(
                items.Count == 0 ? null : items.Min(item => item.DecisionVersion),
                items.Count == 0 ? null : items.Max(item => item.DecisionVersion)),
            new ModerationExportVersionRangeDto(
                items.Count == 0 ? null : items.Min(item => item.CaseVersion),
                items.Count == 0 ? null : items.Max(item => item.CaseVersion)),
            Convert.ToHexStringLower(pageHash.GetHashAndReset()),
            partitions);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record HashedItem(
        ModerationDecisionExportItemDto Item,
        byte[] Hash,
        long DecisionVersion,
        long CaseVersion);
}
