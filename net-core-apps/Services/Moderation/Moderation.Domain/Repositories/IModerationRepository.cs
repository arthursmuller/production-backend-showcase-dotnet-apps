using Moderation.Domain.AggregatesModel;

namespace Moderation.Domain.Repositories;

public sealed record ModerationSlaBacklogState(
    long ActiveCount,
    long OverdueCount,
    DateTimeOffset? OldestOverdueDueAtUtc);

public interface IModerationRepository
{
    Task<ModerationReport?> FindReportByIdempotencyAsync(
        string reporterId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken);

    Task<ModerationReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken);
    Task<ModerationCase?> GetCaseAsync(Guid caseId, CancellationToken cancellationToken);
    Task<ModerationCase?> GetCaseByReportAsync(Guid reportId, CancellationToken cancellationToken);
    Task<LegalHold?> GetActiveLegalHoldAsync(Guid caseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationCase>> GetCasesPageAsync(
        DateTimeOffset? afterReceivedAtUtc,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationCase>> GetOverdueCasesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken);
    Task<ModerationSlaBacklogState> GetSlaBacklogAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationAuditEntry>> GetAuditAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationCaseMessage>> GetMessagesAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken);

    void AddReport(ModerationReport report);
    void AddCase(ModerationCase moderationCase);
    void AddLegalHold(LegalHold legalHold);
    void AddMessage(ModerationCaseMessage message);
    void ClearTracking();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class DuplicateModerationReportException(Exception? innerException = null)
    : Exception("A report already exists for this reporter and idempotency key.", innerException);
