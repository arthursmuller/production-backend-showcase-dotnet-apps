using System.Data;
using Microsoft.EntityFrameworkCore;
using Moderation.Application.Features.Privacy;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;
using MySqlConnector;

namespace Moderation.Infra.Database.Ef.Repositories;

public sealed class ModerationRetentionStore(
    ModerationDbContext context,
    ModerationSubjectPseudonymizer subjectPseudonymizer)
    : IModerationRetentionStore
{
    private const string RetainedActor = "retained";

    public async Task<ModerationRetentionBatchResult> ExecuteBatchAsync(
        DateTimeOffset redactBeforeUtc,
        DateTimeOffset purgeBeforeUtc,
        DateTimeOffset nowUtc,
        string leaseOwner,
        TimeSpan leaseDuration,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 64)
            throw new ArgumentException("A bounded retention lease owner is required.", nameof(leaseOwner));
        if (maxBatchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize));
        if (purgeBeforeUtc > redactBeforeUtc)
            throw new ArgumentException("The audit purge cutoff cannot be newer than the redaction cutoff.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            // Serializable is intentional: the NOT EXISTS(active hold) predicates take
            // next-key locks on the case-scoped hold index, so a concurrently placed hold
            // cannot slip between selection and redaction/deletion.
            IsolationLevel.Serializable,
            cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT IGNORE INTO `ModerationRetentionCheckpoints`
                (`Id`, `FencingToken`, `TotalCasesRedacted`, `TotalCasesPurged`)
            VALUES (1, 0, 0, 0);
            """,
            cancellationToken);

        var leaseUntilUtc = nowUtc.Add(leaseDuration);
        var acquired = await context.RetentionCheckpoints
            .Where(checkpoint => checkpoint.Id == ModerationRetentionCheckpoint.SingletonId
                && (checkpoint.LeaseUntilUtc == null
                    || checkpoint.LeaseUntilUtc <= nowUtc
                    || checkpoint.LeaseOwner == leaseOwner))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(checkpoint => checkpoint.LeaseOwner, leaseOwner)
                    .SetProperty(checkpoint => checkpoint.LeaseUntilUtc, leaseUntilUtc)
                    .SetProperty(checkpoint => checkpoint.LastStartedAtUtc, nowUtc)
                    .SetProperty(checkpoint => checkpoint.FencingToken, checkpoint => checkpoint.FencingToken + 1),
                cancellationToken);

        if (acquired == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ModerationRetentionBatchResult(false, 0, 0, 0);
        }

        var fencingToken = await context.RetentionCheckpoints
            .Where(checkpoint => checkpoint.Id == ModerationRetentionCheckpoint.SingletonId)
            .Select(checkpoint => checkpoint.FencingToken)
            .SingleAsync(cancellationToken);

        // The append-only delete triggers accept destructive maintenance only while this
        // exact durable lease is active. A stale pooled-session value is harmless because
        // the trigger also checks the live checkpoint row and lease expiry.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SET @moderation_retention_fencing_token = {fencingToken}",
            cancellationToken);

        var purged = await PurgeExpiredCasesAsync(
            purgeBeforeUtc,
            fencingToken,
            maxBatchSize,
            cancellationToken);
        var redacted = await RedactExpiredCasesAsync(
            redactBeforeUtc,
            nowUtc,
            fencingToken,
            maxBatchSize,
            cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            "SET @moderation_retention_fencing_token = NULL;",
            cancellationToken);

        var completed = await context.RetentionCheckpoints
            .Where(checkpoint => checkpoint.Id == ModerationRetentionCheckpoint.SingletonId
                && checkpoint.LeaseOwner == leaseOwner
                && checkpoint.FencingToken == fencingToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(checkpoint => checkpoint.LastCompletedAtUtc, nowUtc)
                    .SetProperty(checkpoint => checkpoint.TotalCasesRedacted,
                        checkpoint => checkpoint.TotalCasesRedacted + redacted)
                    .SetProperty(checkpoint => checkpoint.TotalCasesPurged,
                        checkpoint => checkpoint.TotalCasesPurged + purged)
                    .SetProperty(checkpoint => checkpoint.LeaseOwner, (string?)null)
                    .SetProperty(checkpoint => checkpoint.LeaseUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
        if (completed != 1)
            throw new InvalidOperationException("The Moderation retention fencing lease was lost.");

        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return new ModerationRetentionBatchResult(true, redacted, purged, fencingToken);
    }

    private async Task<int> RedactExpiredCasesAsync(
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc,
        long fencingToken,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        var candidates = await context.Cases
            .AsNoTracking()
            .Where(moderationCase => moderationCase.ErasureSubjectUserId == null
                && moderationCase.Status == CaseStatus.Closed
                && moderationCase.RetentionRedactedAtUtc == null
                && moderationCase.LastChangedAtUtc < cutoffUtc
                && !context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive))
            .OrderBy(moderationCase => moderationCase.LastChangedAtUtc)
            .ThenBy(moderationCase => moderationCase.Id)
            .Select(moderationCase => moderationCase.Id)
            .Take(maxBatchSize)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return 0;

        await context.Cases
            .Where(moderationCase => candidates.Contains(moderationCase.Id)
                && moderationCase.ErasureSubjectUserId == null
                && moderationCase.Status == CaseStatus.Closed
                && moderationCase.RetentionRedactedAtUtc == null
                && moderationCase.LastChangedAtUtc < cutoffUtc
                && !context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(moderationCase => moderationCase.RetentionClaimToken, fencingToken)
                    .SetProperty(moderationCase => moderationCase.RetentionRedactedAtUtc, nowUtc),
                cancellationToken);

        var claimed = await context.Cases
            .AsNoTracking()
            .Where(moderationCase => candidates.Contains(moderationCase.Id)
                && moderationCase.RetentionClaimToken == fencingToken
                && moderationCase.RetentionRedactedAtUtc != null)
            .Select(moderationCase => new RetentionCandidate(
                moderationCase.Id,
                moderationCase.ReportId,
                moderationCase.ReporterId,
                moderationCase.ErasureSubjectHash))
            .ToArrayAsync(cancellationToken);
        if (claimed.Length == 0)
            return 0;

        var caseIds = claimed.Select(candidate => candidate.CaseId).ToArray();
        var reportIds = claimed.Select(candidate => candidate.ReportId).ToArray();

        // ReporterId is about to be irreversibly replaced. Persist a keyed,
        // non-reversible lookup first so a later erasure can still find and
        // remove the retained audit chain.
        await PersistErasureSubjectHashesAsync(claimed, cancellationToken);

        await context.CaseMessages
            .Where(message => caseIds.Contains(message.CaseId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.EvidenceReferences
            .Where(evidence => reportIds.Contains(evidence.ReportId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.LegalHolds
            .Where(hold => caseIds.Contains(hold.CaseId) && !hold.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(hold => hold.ResourceId, RetainedActor)
                    .SetProperty(hold => hold.PlacedByActorId, RetainedActor)
                    .SetProperty(hold => hold.ReleasedByActorId, RetainedActor),
                cancellationToken);

        await ExecuteForIdsAsync(
            """
            UPDATE `ModerationReports`
            SET `ReporterId` = CONCAT('retained:', LOWER(`Id`)),
                `ResourceId` = CONCAT('retained:', LOWER(`CaseId`)),
                `IdempotencyKeyHash` = LOWER(SHA2(CONCAT('retained-idempotency:', `Id`), 256)),
                `RequestHash` = LOWER(SHA2(CONCAT('retained-request:', `Id`), 256))
            WHERE `Id` IN ({0});
            """,
            reportIds,
            cancellationToken);
        await ExecuteForIdsAsync(
            """
            UPDATE `ModerationCases`
            SET `ReporterId` = CONCAT('retained:', LOWER(`Id`)),
                `ResourceId` = CONCAT('retained:', LOWER(`Id`)),
                `AssignedInvestigatorId` = NULL
            WHERE `Id` IN ({0});
            """,
            caseIds,
            cancellationToken);

        return claimed.Length;
    }

    private async Task<int> PurgeExpiredCasesAsync(
        DateTimeOffset cutoffUtc,
        long fencingToken,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        var candidates = await context.Cases
            .AsNoTracking()
            .Where(moderationCase => moderationCase.ErasureSubjectUserId == null
                && moderationCase.Status == CaseStatus.Closed
                && moderationCase.LastChangedAtUtc < cutoffUtc
                && !context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive))
            .OrderBy(moderationCase => moderationCase.LastChangedAtUtc)
            .ThenBy(moderationCase => moderationCase.Id)
            .Select(moderationCase => moderationCase.Id)
            .Take(maxBatchSize)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return 0;

        await context.Cases
            .Where(moderationCase => candidates.Contains(moderationCase.Id)
                && moderationCase.ErasureSubjectUserId == null
                && moderationCase.Status == CaseStatus.Closed
                && moderationCase.LastChangedAtUtc < cutoffUtc
                && !context.LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    moderationCase => moderationCase.RetentionClaimToken,
                    fencingToken),
                cancellationToken);

        var claimed = await context.Cases
            .AsNoTracking()
            .Where(moderationCase => candidates.Contains(moderationCase.Id)
                && moderationCase.RetentionClaimToken == fencingToken)
            .Select(moderationCase => new PurgeCandidate(moderationCase.Id, moderationCase.ReportId))
            .ToArrayAsync(cancellationToken);
        if (claimed.Length == 0)
            return 0;

        var caseIds = claimed.Select(candidate => candidate.CaseId).ToArray();
        var reportIds = claimed.Select(candidate => candidate.ReportId).ToArray();

        await context.CaseMessages
            .Where(message => caseIds.Contains(message.CaseId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.LegalHolds
            .Where(hold => caseIds.Contains(hold.CaseId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.AuditEntries
            .Where(audit => caseIds.Contains(audit.CaseId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.Cases
            .Where(moderationCase => caseIds.Contains(moderationCase.Id)
                && moderationCase.RetentionClaimToken == fencingToken)
            .ExecuteDeleteAsync(cancellationToken);
        await context.EvidenceReferences
            .Where(evidence => reportIds.Contains(evidence.ReportId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.Reports
            .Where(report => reportIds.Contains(report.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return claimed.Length;
    }

    private async Task ExecuteForIdsAsync(
        string sqlTemplate,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        var parameters = ids
            .Select((id, index) => new MySqlParameter($"@retentionId{index}", id))
            .Cast<object>()
            .ToArray();
        var placeholders = string.Join(", ", parameters.Cast<MySqlParameter>().Select(parameter => parameter.ParameterName));
        var sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, sqlTemplate, placeholders);
        await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    private async Task PersistErasureSubjectHashesAsync(
        IReadOnlyList<RetentionCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var missing = candidates
            .Where(candidate => candidate.ErasureSubjectHash is null)
            .ToArray();
        if (missing.Length == 0)
            return;

        var parameters = new List<MySqlParameter>(missing.Length * 2);
        var cases = new List<string>(missing.Length);
        var ids = new List<string>(missing.Length);
        for (var index = 0; index < missing.Length; index++)
        {
            var idParameter = new MySqlParameter($"@erasureSubjectId{index}", missing[index].CaseId);
            var hashParameter = new MySqlParameter(
                $"@erasureSubjectHash{index}",
                subjectPseudonymizer.Create(missing[index].ReporterId));
            parameters.Add(idParameter);
            parameters.Add(hashParameter);
            cases.Add($"WHEN {idParameter.ParameterName} THEN {hashParameter.ParameterName}");
            ids.Add(idParameter.ParameterName);
        }

        var sql = $"""
            UPDATE `ModerationCases`
            SET `ErasureSubjectHash` = CASE `Id`
                {string.Join(Environment.NewLine, cases)}
                ELSE `ErasureSubjectHash`
            END
            WHERE `Id` IN ({string.Join(", ", ids)})
              AND `ErasureSubjectHash` IS NULL;
            """;
        var updated = await context.Database.ExecuteSqlRawAsync(
            sql,
            parameters.Cast<object>(),
            cancellationToken);
        if (updated != missing.Length)
        {
            throw new InvalidOperationException(
                "Moderation retention could not preserve every erasure subject lookup.");
        }
    }

    private sealed record RetentionCandidate(
        Guid CaseId,
        Guid ReportId,
        string ReporterId,
        string? ErasureSubjectHash);

    private sealed record PurgeCandidate(Guid CaseId, Guid ReportId);
}
