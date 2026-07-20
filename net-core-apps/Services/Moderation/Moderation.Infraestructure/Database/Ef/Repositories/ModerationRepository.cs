using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;
using MySqlConnector;

namespace Moderation.Infra.Database.Ef.Repositories;

public sealed class ModerationRepository(ModerationDbContext context)
    : IModerationRepository,
      IModerationDecisionExportRepository,
      IModerationDecisionRevisionMaintenanceRepository
{
    internal const string DecisionAuthorityPageSql =
        """
        SELECT `c`.`ExportCursor`, `r`.`CaseId`, `r`.`ResourceKind`, `r`.`ResourceId`,
               `r`.`Action`, `r`.`DecisionVersion`, `r`.`CaseVersion`, `r`.`DecidedAtUtc`,
               `r`.`PartitionKey`, `r`.`RowSha256`
        FROM `ModerationDecisionAuthorityCoordinates` AS `c`
        INNER JOIN `ModerationDecisionStateRevisions` AS `r` FORCE INDEX (`PRIMARY`)
            ON `r`.`RevisionCursor` = (
                SELECT `rr`.`RevisionCursor`
                FROM `ModerationDecisionStateRevisions` AS `rr`
                    FORCE INDEX (`IX_ModerationDecisionRevision_Case_Cursor`)
                WHERE `rr`.`CaseId` = `c`.`CaseId`
                  AND `rr`.`RevisionCursor` <= @revisionHighWatermark
                ORDER BY `rr`.`RevisionCursor` DESC
                LIMIT 1)
        WHERE `c`.`ExportCursor` > @afterCursor
          AND `c`.`ExportCursor` <= @highWatermark
        ORDER BY `c`.`ExportCursor`
        LIMIT @take;
        """;

    public Task<ModerationReport?> FindReportByIdempotencyAsync(
        string reporterId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken) =>
        context.Reports
            .Include(report => report.EvidenceReferences)
            .SingleOrDefaultAsync(
                report => report.ReporterId == reporterId
                    && report.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);

    public Task<ModerationReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken) =>
        context.Reports
            .Include(report => report.EvidenceReferences)
            .SingleOrDefaultAsync(report => report.Id == reportId, cancellationToken);

    public Task<ModerationCase?> GetCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        context.Cases
            .Include(moderationCase => moderationCase.DecisionState)
            .SingleOrDefaultAsync(moderationCase => moderationCase.Id == caseId, cancellationToken);

    public Task<ModerationCase?> GetCaseByReportAsync(Guid reportId, CancellationToken cancellationToken) =>
        context.Cases.SingleOrDefaultAsync(moderationCase => moderationCase.ReportId == reportId, cancellationToken);

    public async Task<ModerationDecisionExportSnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken)
            : null;
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        var dbTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var highWatermark = await ReadLongAsync(
            connection,
            dbTransaction,
            "SELECT COALESCE(MAX(`ExportCursor`), 0) FROM `ModerationDecisionAuthorityCoordinates`;",
            cancellationToken);
        var revisionHighWatermark = await context.DecisionStateRevisions
            .AsNoTracking()
            .MaxAsync(revision => (long?)revision.RevisionCursor, cancellationToken)
            ?? 0;
        var partitions = await ReadModerationPartitionManifestAsync(cancellationToken);
        var minimumDecisionVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `DecisionVersion` FROM `ModerationDecisionAuthorityCurrentRows` FORCE INDEX (`IX_ModerationDecisionAuthorityCurrentRows_DecisionVersion`) ORDER BY `DecisionVersion`, `CaseId` LIMIT 1;",
            cancellationToken);
        var maximumDecisionVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `DecisionVersion` FROM `ModerationDecisionAuthorityCurrentRows` FORCE INDEX (`IX_ModerationDecisionAuthorityCurrentRows_DecisionVersion`) ORDER BY `DecisionVersion` DESC, `CaseId` DESC LIMIT 1;",
            cancellationToken);
        var minimumCaseVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `CaseVersion` FROM `ModerationDecisionAuthorityCurrentRows` FORCE INDEX (`IX_ModerationDecisionAuthorityCurrentRows_CaseVersion`) ORDER BY `CaseVersion`, `CaseId` LIMIT 1;",
            cancellationToken);
        var maximumCaseVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `CaseVersion` FROM `ModerationDecisionAuthorityCurrentRows` FORCE INDEX (`IX_ModerationDecisionAuthorityCurrentRows_CaseVersion`) ORDER BY `CaseVersion` DESC, `CaseId` DESC LIMIT 1;",
            cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new ModerationDecisionExportSnapshot(
            highWatermark,
            revisionHighWatermark,
            DateTimeOffset.UtcNow,
            partitions.Sum(item => item.ItemCount),
            minimumDecisionVersion,
            maximumDecisionVersion,
            minimumCaseVersion,
            maximumCaseVersion,
            CombineXor(partitions.Select(item => item.XorSha256)));
    }

    public async Task<ModerationDecisionExportSlice> ReadPageAsync(
        long afterCursor,
        long snapshotHighWatermark,
        long snapshotRevisionHighWatermark,
        int take,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = DecisionAuthorityPageSql;
        command.Parameters.Add(new MySqlParameter("@revisionHighWatermark", snapshotRevisionHighWatermark));
        command.Parameters.Add(new MySqlParameter("@afterCursor", afterCursor));
        command.Parameters.Add(new MySqlParameter("@highWatermark", snapshotHighWatermark));
        command.Parameters.Add(new MySqlParameter("@take", take + 1));
        var rows = new List<ModerationDecisionExportRow>(take + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ModerationDecisionExportRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                (ResourceKind)reader.GetInt32(2),
                reader.GetString(3),
                (DecisionAction)reader.GetInt32(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
                reader.GetByte(8),
                reader.GetString(9)));
        }
        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        return new ModerationDecisionExportSlice(rows, hasMore);
    }

    private async Task<IReadOnlyList<ModerationDecisionExportPartition>>
        ReadModerationPartitionManifestAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            SELECT `PartitionKey`, `ItemCount`, `XorSha256`
            FROM `ModerationDecisionAuthorityPartitionManifests`
            WHERE `ItemCount` > 0
            ORDER BY `PartitionKey`;
            """;
        var result = new List<ModerationDecisionExportPartition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ModerationDecisionExportPartition(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2).ToLowerInvariant()));
        }
        return result;
    }

    private static async Task<long> ReadLongAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long?> ReadIndexedVersionBoundaryAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CombineXor(IEnumerable<string> hashes)
    {
        var combined = new byte[32];
        foreach (var hash in hashes)
        {
            var bytes = Convert.FromHexString(hash);
            for (var index = 0; index < combined.Length; index++)
                combined[index] ^= bytes[index];
        }
        return Convert.ToHexStringLower(combined);
    }

    public async Task<ModerationDecisionRevisionRetentionState> GetRetentionStateAsync(
        DateTimeOffset cutoffUtc,
        int maximumSampleSize,
        CancellationToken cancellationToken)
    {
        if (maximumSampleSize is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maximumSampleSize));
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var total = await ReadLongAsync(
            connection,
            transaction,
            "SELECT COALESCE(MAX(`TABLE_ROWS`), 0) FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'ModerationDecisionStateRevisions';",
            cancellationToken);
        await using var sample = connection.CreateCommand();
        sample.Transaction = transaction;
        sample.CommandText =
            """
            SELECT `RecordedAtUtc`
            FROM `ModerationDecisionRevisionPruneCandidates`
                FORCE INDEX (`IX_ModerationDecisionRevisionPruneCandidates_Retention`)
            WHERE `RecordedAtUtc` < @cutoffUtc
            ORDER BY `RecordedAtUtc`, `RevisionCursor`
            LIMIT @maximum;
            """;
        sample.Parameters.Add(new MySqlParameter("@cutoffUtc", cutoffUtc.UtcDateTime));
        sample.Parameters.Add(new MySqlParameter("@maximum", maximumSampleSize + 1));
        var supersededAt = new List<DateTimeOffset>(maximumSampleSize + 1);
        await using (var reader = await sample.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                supersededAt.Add(new DateTimeOffset(
                    DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)));
            }
        }
        return new ModerationDecisionRevisionRetentionState(
            total,
            Math.Min(supersededAt.Count, maximumSampleSize),
            supersededAt.Count > maximumSampleSize,
            supersededAt.Count == 0 ? null : supersededAt[0]);
    }

    public async Task<int> PruneSupersededAsync(
        DateTimeOffset cutoffUtc,
        int maximum,
        CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var ownedTransaction = context.Database.CurrentTransaction is null
            ? await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var transaction = ownedTransaction ?? context.Database.CurrentTransaction?.GetDbTransaction();
        var candidates = new List<long>(maximum);
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText =
                """
                SELECT `RevisionCursor`
                FROM `ModerationDecisionRevisionPruneCandidates`
                    FORCE INDEX (`IX_ModerationDecisionRevisionPruneCandidates_Retention`)
                WHERE `RecordedAtUtc` < @cutoffUtc
                ORDER BY `RecordedAtUtc`, `RevisionCursor`
                LIMIT @maximum
                FOR UPDATE SKIP LOCKED;
                """;
            claim.Parameters.Add(new MySqlParameter("@cutoffUtc", cutoffUtc.UtcDateTime));
            claim.Parameters.Add(new MySqlParameter("@maximum", maximum));
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                candidates.Add(reader.GetInt64(0));
        }
        if (candidates.Count == 0)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return 0;
        }
        var bypassEnabled = false;
        try
        {
            await using (var enable = connection.CreateCommand())
            {
                enable.Transaction = transaction;
                enable.CommandText = "SET @moderation_decision_revision_prune = 1;";
                await enable.ExecuteNonQueryAsync(cancellationToken);
                bypassEnabled = true;
            }
            await using var prune = connection.CreateCommand();
            prune.Transaction = transaction;
            prune.CommandText = $"DELETE FROM `ModerationDecisionStateRevisions` WHERE `RevisionCursor` IN ({Parameters(candidates.Count)});";
            AddCursorParameters(prune, candidates);
            var pruned = await prune.ExecuteNonQueryAsync(cancellationToken);
            await ResetModerationPruneBypassAsync(connection, transaction);
            bypassEnabled = false;

            await using var removeClaims = connection.CreateCommand();
            removeClaims.Transaction = transaction;
            removeClaims.CommandText = $"DELETE FROM `ModerationDecisionRevisionPruneCandidates` WHERE `RevisionCursor` IN ({Parameters(candidates.Count)});";
            AddCursorParameters(removeClaims, candidates);
            await removeClaims.ExecuteNonQueryAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return pruned;
        }
        finally
        {
            if (bypassEnabled)
                await ResetModerationPruneBypassAsync(connection, transaction);
        }
    }

    private static string Parameters(int count) =>
        string.Join(",", Enumerable.Range(0, count).Select(index => $"@cursor{index}"));

    private static void AddCursorParameters(
        System.Data.Common.DbCommand command,
        IReadOnlyList<long> cursors)
    {
        for (var index = 0; index < cursors.Count; index++)
            command.Parameters.Add(new MySqlParameter($"@cursor{index}", cursors[index]));
    }

    private static async Task ResetModerationPruneBypassAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction)
    {
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = "SET @moderation_decision_revision_prune = NULL;";
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await disable.ExecuteNonQueryAsync(cleanup.Token);
    }

    public Task<LegalHold?> GetActiveLegalHoldAsync(Guid caseId, CancellationToken cancellationToken) =>
        context.LegalHolds.SingleOrDefaultAsync(
            hold => hold.CaseId == caseId && hold.IsActive,
            cancellationToken);

    public async Task<IReadOnlyList<ModerationCase>> GetCasesPageAsync(
        DateTimeOffset? afterReceivedAtUtc,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.Cases.AsNoTracking();
        if (afterReceivedAtUtc.HasValue && afterId.HasValue)
        {
            query = query.Where(moderationCase =>
                moderationCase.ReceivedAtUtc < afterReceivedAtUtc.Value
                || (moderationCase.ReceivedAtUtc == afterReceivedAtUtc.Value
                    && moderationCase.Id.CompareTo(afterId.Value) < 0));
        }

        return await query
            .OrderByDescending(moderationCase => moderationCase.ReceivedAtUtc)
            .ThenByDescending(moderationCase => moderationCase.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModerationCase>> GetOverdueCasesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken) =>
        await context.Cases
            .Where(moderationCase =>
                moderationCase.Status != CaseStatus.Decided
                && moderationCase.Status != CaseStatus.Closed
                && moderationCase.EscalatedAtUtc == null
                && moderationCase.SlaDueAtUtc <= now)
            .OrderBy(moderationCase => moderationCase.SlaDueAtUtc)
            .ThenBy(moderationCase => moderationCase.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<ModerationSlaBacklogState> GetSlaBacklogAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var aggregate = await context.Cases
            .AsNoTracking()
            .Where(moderationCase =>
                moderationCase.Status != CaseStatus.Decided
                && moderationCase.Status != CaseStatus.Closed)
            .GroupBy(_ => 1)
            .Select(group => new ModerationSlaBacklogState(
                group.LongCount(),
                group.Sum(moderationCase => moderationCase.SlaDueAtUtc <= now ? 1L : 0L),
                group
                    .Where(moderationCase => moderationCase.SlaDueAtUtc <= now)
                    .Select(moderationCase => (DateTimeOffset?)moderationCase.SlaDueAtUtc)
                    .Min()))
            .SingleOrDefaultAsync(cancellationToken);
        return aggregate ?? new ModerationSlaBacklogState(0, 0, null);
    }

    public async Task<IReadOnlyList<ModerationAuditEntry>> GetAuditAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken) =>
        await context.AuditEntries
            .AsNoTracking()
            .Where(entry => entry.CaseId == caseId)
            .OrderByDescending(entry => entry.CaseVersion)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ModerationCaseMessage>> GetMessagesAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken) =>
        await context.CaseMessages
            .AsNoTracking()
            .Where(message => message.CaseId == caseId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public void AddReport(ModerationReport report) => context.Reports.Add(report);
    public void AddCase(ModerationCase moderationCase) => context.Cases.Add(moderationCase);
    public void AddLegalHold(LegalHold legalHold) => context.LegalHolds.Add(legalHold);
    public void AddMessage(ModerationCaseMessage message) => context.CaseMessages.Add(message);
    public void ClearTracking() => context.ChangeTracker.Clear();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var caseEntry = exception.Entries.FirstOrDefault(entry => entry.Entity is ModerationCase);
            if (caseEntry is null)
                throw;
            var expected = (long)(caseEntry.OriginalValues[nameof(ModerationCase.Version)] ?? 0L);
            var databaseValues = await caseEntry.GetDatabaseValuesAsync(cancellationToken);
            var actual = databaseValues is null
                ? expected
                : (long)(databaseValues[nameof(ModerationCase.Version)] ?? expected);
            throw new ModerationConcurrencyException(expected, actual);
        }
    }
}
