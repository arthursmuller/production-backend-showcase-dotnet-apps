using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Queries;

public sealed class CaseQueries(IModerationRepository repository)
{
    public async Task<ModerationResult<ModerationCaseDetails>> GetInternalCaseAsync(
        Guid caseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var moderationCase = await repository.GetCaseAsync(caseId, cancellationToken);
        if (moderationCase is null)
        {
            return ModerationFeatureSupport.NotFound<ModerationCaseDetails>(
                "case-not-found",
                "The case was not found.");
        }
        var report = await repository.GetReportAsync(moderationCase.ReportId, cancellationToken);
        if (report is null)
        {
            return ModerationFeatureSupport.NotFound<ModerationCaseDetails>(
                "report-not-found",
                "The case report was not found.");
        }
        return ModerationResult<ModerationCaseDetails>.Success(
            ModerationFeatureSupport.MapDetails(moderationCase, report));
    }

    public async Task<ModerationResult<CursorPage<ModerationCaseSummary>>> ListInternalCasesAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ModerationCursor.TryDecode(cursor, out var afterTime, out var afterId))
        {
            return ModerationFeatureSupport.Validation<CursorPage<ModerationCaseSummary>>(
                "The pagination cursor is invalid.");
        }
        var boundedSize = Math.Clamp(pageSize, 1, 100);
        var cases = await repository.GetCasesPageAsync(afterTime, afterId, boundedSize + 1, cancellationToken);
        var hasMore = cases.Count > boundedSize;
        var pageItems = cases.Take(boundedSize).ToArray();
        var nextCursor = hasMore && pageItems.Length > 0
            ? ModerationCursor.Encode(pageItems[^1].ReceivedAtUtc, pageItems[^1].Id)
            : null;
        return ModerationResult<CursorPage<ModerationCaseSummary>>.Success(
            new CursorPage<ModerationCaseSummary>(
                pageItems.Select(ModerationFeatureSupport.MapSummary).ToArray(),
                nextCursor));
    }

    public async Task<ModerationResult<IReadOnlyList<AuditEntryDetails>>> GetAuditAsync(
        Guid caseId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await repository.GetCaseAsync(caseId, cancellationToken) is null)
        {
            return ModerationFeatureSupport.NotFound<IReadOnlyList<AuditEntryDetails>>(
                "case-not-found",
                "The case was not found.");
        }
        var entries = await repository.GetAuditAsync(caseId, Math.Clamp(pageSize, 1, 100), cancellationToken);
        return ModerationResult<IReadOnlyList<AuditEntryDetails>>.Success(
            entries.Select(entry => new AuditEntryDetails(
                entry.Id,
                entry.ActorId,
                entry.Action,
                entry.ReasonCode,
                entry.CaseVersion,
                entry.PreviousHash,
                entry.Hash,
                entry.OccurredAtUtc)).ToArray());
    }

    public async Task<ModerationResult<IReadOnlyList<CaseMessageDetails>>> GetCaseMessagesAsync(
        Guid caseId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await repository.GetCaseAsync(caseId, cancellationToken) is null)
        {
            return ModerationFeatureSupport.NotFound<IReadOnlyList<CaseMessageDetails>>(
                "case-not-found",
                "The case was not found.");
        }
        var messages = await repository.GetMessagesAsync(
            caseId,
            Math.Clamp(pageSize, 1, 100),
            cancellationToken);
        return ModerationResult<IReadOnlyList<CaseMessageDetails>>.Success(
            messages.Select(ModerationFeatureSupport.MapMessage).ToArray());
    }
}
