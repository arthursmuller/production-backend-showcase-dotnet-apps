using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Interaction.Application.Dtos;
using Interaction.Application.Options;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;

namespace Interaction.Application.Features.Exports.Queries;

public sealed record ExportInteractionAuthoritativeStateRequest(
    string? Cursor,
    int? PageSize);

public sealed record ExportInteractionAuthoritativeStateQuery(
    IInteractionAuthoritativeExportRepository Repository,
    InteractionAuthoritativeExportCursor CursorCodec,
    TimeProvider TimeProvider)
    : Query<ExportInteractionAuthoritativeStateRequest, InteractionAuthoritativeExportPageDto>
{
    private const int PartitionCount = 256;
    private const string Canonicalization = "interaction-authority/sha256";

    public override async Task<InteractionAuthoritativeExportPageDto> Handle(
        ExportInteractionAuthoritativeStateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (afterCursor, pageSize, snapshot, issuedAtUtc) =
            await ResolveRequestAsync(request, cancellationToken);
        var slice = await Repository.ReadPageAsync(
            afterCursor,
            snapshot.HighWatermark,
            snapshot.RevisionHighWatermark,
            pageSize,
            cancellationToken);

        var materialized = slice.Items.Select(ToHashedItem).ToArray();
        var items = materialized.Select(item => item.Item).ToArray();
        var nextCursor = slice.HasMore && slice.Items.Count > 0
            ? CursorCodec.Encode(new InteractionAuthoritativeExportCursorState(
                slice.Items[^1].Cursor,
                pageSize,
                snapshot,
                issuedAtUtc))
            : null;

        return new InteractionAuthoritativeExportPageDto(
            items,
            nextCursor,
            slice.HasMore,
            new InteractionAuthoritativeExportSnapshotDto(
                snapshot.HighWatermark,
                snapshot.RevisionHighWatermark,
                snapshot.CapturedAtUtc,
                snapshot.ExpectedItemCount,
                new ExportVersionRangeDto(
                    snapshot.MinimumResourceVersion,
                    snapshot.MaximumResourceVersion),
                new ExportVersionRangeDto(
                    snapshot.MinimumSummaryVersion,
                    snapshot.MaximumSummaryVersion),
                PartitionCount,
                snapshot.GlobalXorSha256),
            Integrity(materialized));
    }

    private async Task<(
        long After,
        int PageSize,
        InteractionAuthoritativeExportSnapshot Snapshot,
        DateTimeOffset IssuedAtUtc)>
        ResolveRequestAsync(
            ExportInteractionAuthoritativeStateRequest request,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Cursor))
        {
            var pageSize = request.PageSize ?? AuthoritativeExportOptions.MaximumPageSize;
            ValidatePageSize(pageSize);
            return (
                0,
                pageSize,
                await Repository.CaptureSnapshotAsync(cancellationToken),
                TimeProvider.GetUtcNow());
        }

        var cursor = CursorCodec.Decode(request.Cursor);
        if (request.PageSize.HasValue && request.PageSize.Value != cursor.PageSize)
        {
            throw new InteractionValidationException(
                "A continuation request must retain the first page's page size.");
        }
        return (cursor.AfterCursor, cursor.PageSize, cursor.Snapshot, cursor.IssuedAtUtc);
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > AuthoritativeExportOptions.MaximumPageSize)
        {
            throw new InteractionValidationException(
                $"Authoritative export page size must be between 1 and {AuthoritativeExportOptions.MaximumPageSize}.");
        }
    }

    private static HashedItem ToHashedItem(InteractionAuthoritativeExportRow row)
    {
        var hash = Convert.FromHexString(row.RowSha256);
        var partition = row.PartitionKey;
        var summary = row.SummaryVersion.HasValue
            ? new InteractionEngagementSummaryExportDto(
                row.LikeCount!.Value,
                row.DislikeCount!.Value,
                row.RatingCount!.Value,
                row.RatingTotal!.Value,
                row.ViewCount!.Value,
                row.CommentCount!.Value,
                row.ShareCount!.Value,
                row.ContributionSourceVersion!.Value,
                row.ContributionHighestSeenVersion!.Value,
                row.ContributionHasGap!.Value,
                row.HasExternalBaseline!.Value,
                row.IsErased!.Value,
                row.SummaryVersion.Value,
                row.SummaryUpdatedAtUtc!.Value)
            : null;
        var item = new InteractionAuthoritativeExportItemDto(
            row.Cursor,
            row.ResourceType.ToString().ToLowerInvariant(),
            row.ResourceId,
            new InteractionResourceStateExportDto(
                row.Lifecycle.ToString().ToLowerInvariant(),
                row.Visibility.ToString().ToLowerInvariant(),
                row.AppliedResourceVersion,
                row.HighestSeenResourceVersion,
                row.ResourceHasGap,
                row.ResourceConcurrencyVersion,
                row.ResourceUpdatedAtUtc),
            summary,
            partition,
            row.RowSha256);
        return new HashedItem(item, hash, row.AppliedResourceVersion, row.SummaryVersion);
    }

    private static InteractionAuthoritativeExportIntegrityDto Integrity(
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
                return new ExportPartitionChecksumDto(
                    group.Key,
                    group.Count(),
                    Convert.ToHexStringLower(xor));
            })
            .ToArray();

        return new InteractionAuthoritativeExportIntegrityDto(
            Canonicalization,
            items.Count,
            new ExportVersionRangeDto(
                items.Count == 0 ? null : items.Min(item => item.ResourceVersion),
                items.Count == 0 ? null : items.Max(item => item.ResourceVersion)),
            new ExportVersionRangeDto(
                items.Where(item => item.SummaryVersion.HasValue)
                    .Select(item => item.SummaryVersion)
                    .DefaultIfEmpty(null)
                    .Min(),
                items.Where(item => item.SummaryVersion.HasValue)
                    .Select(item => item.SummaryVersion)
                    .DefaultIfEmpty(null)
                    .Max()),
            Convert.ToHexStringLower(pageHash.GetHashAndReset()),
            partitions);
    }

    private sealed record HashedItem(
        InteractionAuthoritativeExportItemDto Item,
        byte[] Hash,
        long ResourceVersion,
        long? SummaryVersion);
}
