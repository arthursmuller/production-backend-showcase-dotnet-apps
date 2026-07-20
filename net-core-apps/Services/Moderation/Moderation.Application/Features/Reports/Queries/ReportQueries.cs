using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Reports.Queries;

public sealed class ReportQueries(
    IModerationRepository repository,
    IModerationUserErasureStore erasures)
{
    public async Task<ModerationResult<ModerationReportReceiptDto>> GetReceiptAsync(
        Guid reportId,
        string reporterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reporterId = ModerationSubject.Normalize(reporterId);
        if (await IsErasedAsync(reporterId, cancellationToken))
        {
            return ModerationFeatureSupport.NotFound<ModerationReportReceiptDto>(
                "report-not-found",
                "The report was not found.");
        }
        var report = await repository.GetReportAsync(reportId, cancellationToken);
        if (report is null || !string.Equals(report.ReporterId, reporterId, StringComparison.Ordinal))
        {
            return ModerationFeatureSupport.NotFound<ModerationReportReceiptDto>(
                "report-not-found",
                "The report was not found.");
        }

        return ModerationResult<ModerationReportReceiptDto>.Success(
            new ModerationReportReceiptDto(report.Id, "Received", report.ReceivedAtUtc, false));
    }

    public async Task<ModerationResult<ModerationReportStatusDto>> GetReporterStatusAsync(
        Guid reportId,
        string reporterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reporterId = ModerationSubject.Normalize(reporterId);
        if (await IsErasedAsync(reporterId, cancellationToken))
        {
            return ModerationFeatureSupport.NotFound<ModerationReportStatusDto>(
                "report-not-found",
                "The report was not found.");
        }
        var report = await repository.GetReportAsync(reportId, cancellationToken);
        if (report is null || !string.Equals(report.ReporterId, reporterId, StringComparison.Ordinal))
        {
            return ModerationFeatureSupport.NotFound<ModerationReportStatusDto>(
                "report-not-found",
                "The report was not found.");
        }

        var moderationCase = await repository.GetCaseByReportAsync(reportId, cancellationToken);
        if (moderationCase is null)
        {
            return ModerationFeatureSupport.NotFound<ModerationReportStatusDto>(
                "case-not-found",
                "The report status was not found.");
        }

        return ModerationResult<ModerationReportStatusDto>.Success(
            new ModerationReportStatusDto(
                report.Id,
                ModerationFeatureSupport.ToReporterStatus(moderationCase.Status),
                moderationCase.LastChangedAtUtc,
                moderationCase.CanAppeal,
                moderationCase.Version));
    }

    private Task<bool> IsErasedAsync(
        string reporterId,
        CancellationToken cancellationToken) =>
        ModerationSubject.TryGetUserId(reporterId, out var userId)
            ? erasures.IsUserErasedAsync(userId, cancellationToken)
            : Task.FromResult(false);
}
