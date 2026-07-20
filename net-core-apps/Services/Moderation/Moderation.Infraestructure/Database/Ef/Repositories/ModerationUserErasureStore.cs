using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moderation.Application.Features.Privacy;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;

namespace Moderation.Infra.Database.Ef.Repositories;

public sealed class ModerationUserErasureStore(
    ModerationDbContext context,
    ModerationSubjectPseudonymizer subjectPseudonymizer)
    : IModerationUserErasureStore
{
    public async ValueTask<IModerationUserErasureTransaction> BeginSerializableAsync(
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            return BorrowedTransaction.Instance;
        return new ErasureTransaction(
            await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken));
    }

    public Task<ModerationUserErasure?> GetForUpdateAsync(
        long userId,
        CancellationToken cancellationToken) =>
        context.UserErasures
            .FromSqlInterpolated(
                $"SELECT * FROM `ModerationUserErasures` WHERE `UserId` = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsUserErasedAsync(
        long userId,
        CancellationToken cancellationToken) =>
        context.UserErasures.AsNoTracking().AnyAsync(
            erasure => erasure.UserId == userId,
            cancellationToken);

    public void Add(ModerationUserErasure erasure) =>
        context.UserErasures.Add(erasure);

    public Task<ModerationUserErasure?> LockNextDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.UserErasures.FromSqlInterpolated($"""
            SELECT *
            FROM `ModerationUserErasures`
            WHERE `NextAttemptAtUtc` IS NOT NULL
              AND `NextAttemptAtUtc` <= {now}
              AND (
                    `Stage` = {(byte)ModerationUserErasureStage.Active}
                    OR (
                        `Stage` = {(byte)ModerationUserErasureStage.Completed}
                        AND `HeldCaseCount` > 0
                    )
                  )
              AND (`LeaseUntilUtc` IS NULL OR `LeaseUntilUtc` <= {now})
            ORDER BY `NextAttemptAtUtc`,
                     COALESCE(`LastProcessedAtUtc`, `RequestedAtUtc`),
                     `UserId`
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);

    public async Task<ModerationErasureDataBatch> ExecuteDataBatchAsync(
        long userId,
        long fencingToken,
        int maximum,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken));
        if (maximum is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Moderation erasure batches require an active serializable transaction.");

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SET @moderation_erasure_user_id = {userId}, @moderation_erasure_fencing_token = {fencingToken}",
            cancellationToken);
        try
        {
            var reporterId = userId.ToString(CultureInfo.InvariantCulture);
            var subjectHash = subjectPseudonymizer.Create(userId);
            var caseIds = await context.Cases
                .AsNoTracking()
                .Where(moderationCase =>
                    (moderationCase.ErasureSubjectUserId == userId
                        || (moderationCase.ErasureSubjectUserId == null
                            && (moderationCase.ReporterId == reporterId
                                || moderationCase.ErasureSubjectHash == subjectHash)))
                    && !context.LegalHolds.Any(hold =>
                        hold.CaseId == moderationCase.Id && hold.IsActive))
                .OrderBy(moderationCase =>
                    moderationCase.ErasureSubjectUserId == userId ? 0 : 1)
                .ThenBy(moderationCase => moderationCase.ReceivedAtUtc)
                .ThenBy(moderationCase => moderationCase.Id)
                .Select(moderationCase => moderationCase.Id)
                .Take(maximum)
                .ToArrayAsync(cancellationToken);

            var claimed = caseIds.Length == 0
                ? 0
                : await context.Cases
                    .Where(moderationCase => caseIds.Contains(moderationCase.Id)
                        && !context.LegalHolds.Any(hold =>
                            hold.CaseId == moderationCase.Id && hold.IsActive))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                moderationCase => moderationCase.ErasureSubjectUserId,
                                userId)
                            .SetProperty(
                                moderationCase => moderationCase.ErasureClaimToken,
                                fencingToken),
                        cancellationToken);

            var messageIds = await ClaimedCases(fencingToken, userId)
                .SelectMany(moderationCase => context.CaseMessages
                    .Where(message => message.CaseId == moderationCase.Id))
                .OrderBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Id)
                .Select(message => message.Id)
                .Take(maximum)
                .ToArrayAsync(cancellationToken);
            var messages = messageIds.Length == 0
                ? 0
                : await context.CaseMessages
                    .Where(message => messageIds.Contains(message.Id))
                    .ExecuteDeleteAsync(cancellationToken);

            var auditIds = await ClaimedCases(fencingToken, userId)
                .SelectMany(moderationCase => context.AuditEntries
                    .Where(audit => audit.CaseId == moderationCase.Id))
                .OrderBy(audit => audit.OccurredAtUtc)
                .ThenBy(audit => audit.Id)
                .Select(audit => audit.Id)
                .Take(maximum)
                .ToArrayAsync(cancellationToken);
            var audits = auditIds.Length == 0
                ? 0
                : await context.AuditEntries
                    .Where(audit => auditIds.Contains(audit.Id))
                    .ExecuteDeleteAsync(cancellationToken);

            var evidenceIds = await ClaimedCases(fencingToken, userId)
                .SelectMany(moderationCase => context.EvidenceReferences
                    .Where(evidence => evidence.ReportId == moderationCase.ReportId))
                .OrderBy(evidence => evidence.Id)
                .Select(evidence => evidence.Id)
                .Take(maximum)
                .ToArrayAsync(cancellationToken);
            var evidence = evidenceIds.Length == 0
                ? 0
                : await context.EvidenceReferences
                    .Where(reference => evidenceIds.Contains(reference.Id))
                    .ExecuteDeleteAsync(cancellationToken);

            var releasedHoldIds = await ClaimedCases(fencingToken, userId)
                .SelectMany(moderationCase => context.LegalHolds
                    .Where(hold => hold.CaseId == moderationCase.Id && !hold.IsActive))
                .OrderBy(hold => hold.PlacedAtUtc)
                .ThenBy(hold => hold.Id)
                .Select(hold => hold.Id)
                .Take(maximum)
                .ToArrayAsync(cancellationToken);
            var releasedHolds = releasedHoldIds.Length == 0
                ? 0
                : await context.LegalHolds
                    .Where(hold => releasedHoldIds.Contains(hold.Id))
                    .ExecuteDeleteAsync(cancellationToken);

            var ready = await ClaimedCases(fencingToken, userId)
                .Where(moderationCase =>
                    !context.CaseMessages.Any(message =>
                        message.CaseId == moderationCase.Id)
                    && !context.AuditEntries.Any(audit =>
                        audit.CaseId == moderationCase.Id)
                    && !context.EvidenceReferences.Any(reference =>
                        reference.ReportId == moderationCase.ReportId)
                    && !context.LegalHolds.Any(hold =>
                        hold.CaseId == moderationCase.Id))
                .OrderBy(moderationCase => moderationCase.ReceivedAtUtc)
                .ThenBy(moderationCase => moderationCase.Id)
                .Select(moderationCase => new
                {
                    CaseId = moderationCase.Id,
                    moderationCase.ReportId,
                })
                .Take(maximum)
                .ToArrayAsync(cancellationToken);

            var readyCaseIds = ready.Select(item => item.CaseId).ToArray();
            var readyReportIds = ready.Select(item => item.ReportId).ToArray();
            var casesDeleted = readyCaseIds.Length == 0
                ? 0
                : await context.Cases
                    .Where(moderationCase => readyCaseIds.Contains(moderationCase.Id)
                        && moderationCase.ErasureSubjectUserId == userId
                        && moderationCase.ErasureClaimToken == fencingToken)
                    .ExecuteDeleteAsync(cancellationToken);
            if (casesDeleted > 0)
            {
                var reportsDeleted = await context.Reports
                    .Where(report => readyReportIds.Contains(report.Id))
                    .ExecuteDeleteAsync(cancellationToken);
                if (reportsDeleted != casesDeleted)
                    throw new InvalidOperationException(
                        "Moderation erasure did not delete matching report/case pairs.");
            }

            return new ModerationErasureDataBatch(
                claimed,
                messages,
                audits,
                evidence,
                releasedHolds,
                casesDeleted);
        }
        finally
        {
            // Session variables must be cleared even when caller work is
            // canceled, but cleanup is bounded so a bad connection cannot hang a worker.
            using var sessionResetTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            await context.Database.ExecuteSqlRawAsync(
                "SET @moderation_erasure_user_id = NULL, @moderation_erasure_fencing_token = NULL;",
                sessionResetTimeout.Token);
        }
    }

    public Task<bool> HasDeletableDataAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var reporterId = userId.ToString(CultureInfo.InvariantCulture);
        var subjectHash = subjectPseudonymizer.Create(userId);
        return context.Cases.AsNoTracking().AnyAsync(
            moderationCase =>
                (moderationCase.ErasureSubjectUserId == userId
                    || (moderationCase.ErasureSubjectUserId == null
                        && (moderationCase.ReporterId == reporterId
                            || moderationCase.ErasureSubjectHash == subjectHash)))
                && !context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive),
            cancellationToken);
    }

    public Task<int> CountActiveHeldCasesAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var reporterId = userId.ToString(CultureInfo.InvariantCulture);
        var subjectHash = subjectPseudonymizer.Create(userId);
        return context.Cases.AsNoTracking().CountAsync(
            moderationCase =>
                (moderationCase.ErasureSubjectUserId == userId
                    || moderationCase.ReporterId == reporterId
                    || moderationCase.ErasureSubjectHash == subjectHash)
                && context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive),
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    private IQueryable<ModerationCase> ClaimedCases(
        long fencingToken,
        long userId) =>
        context.Cases.AsNoTracking().Where(moderationCase =>
            moderationCase.ErasureSubjectUserId == userId
            && moderationCase.ErasureClaimToken == fencingToken
            && !context.LegalHolds.Any(hold =>
                hold.CaseId == moderationCase.Id && hold.IsActive));

    private sealed class ErasureTransaction(IDbContextTransaction transaction)
        : IModerationUserErasureTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class BorrowedTransaction
        : IModerationUserErasureTransaction
    {
        internal static BorrowedTransaction Instance { get; } = new();
        public Task CommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
