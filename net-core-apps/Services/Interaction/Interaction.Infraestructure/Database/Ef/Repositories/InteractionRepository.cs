using System.Data;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using Interaction.Infraestructure.Database.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Interaction.Infraestructure.Database.Ef.Repositories;

public sealed class InteractionRepository(InteractionDbContext context)
    : IInteractionRepository,
      IInteractionAuthoritativeExportRepository,
      IInteractionAuthorityRevisionMaintenanceRepository
{
    internal const string AuthoritativePageSql =
        """
        SELECT `c`.`Cursor`, `r`.`ResourceType`, `r`.`ResourceId`, `r`.`Lifecycle`,
               `r`.`Visibility`, `r`.`AppliedResourceVersion`, `r`.`HighestSeenResourceVersion`,
               `r`.`ResourceHasGap`, `r`.`ResourceConcurrencyVersion`, `r`.`ResourceUpdatedAtUtc`,
               `r`.`LikeCount`, `r`.`DislikeCount`, `r`.`RatingCount`, `r`.`RatingTotal`,
               `r`.`ViewCount`, `r`.`CommentCount`, `r`.`ShareCount`,
               `r`.`ContributionSourceVersion`, `r`.`ContributionHighestSeenVersion`,
               `r`.`ContributionHasGap`, `r`.`HasExternalBaseline`, `r`.`IsErased`,
               `r`.`SummaryVersion`, `r`.`SummaryUpdatedAtUtc`, `r`.`PartitionKey`, `r`.`RowSha256`
        FROM `InteractionAuthorityCoordinates` AS `c`
        INNER JOIN `InteractionAuthoritativeStateRevisions` AS `r` FORCE INDEX (`PRIMARY`)
            ON `r`.`RevisionCursor` = (
                SELECT `rr`.`RevisionCursor`
                FROM `InteractionAuthoritativeStateRevisions` AS `rr`
                    FORCE INDEX (`IX_InteractionAuthorityRevision_Resource_Cursor`)
                WHERE `rr`.`ResourceType` = `c`.`ResourceType`
                  AND `rr`.`ResourceId` = `c`.`ResourceId`
                  AND `rr`.`RevisionCursor` <= @revisionHighWatermark
                ORDER BY `rr`.`RevisionCursor` DESC
                LIMIT 1)
        WHERE `c`.`Cursor` > @afterCursor
          AND `c`.`Cursor` <= @highWatermark
        ORDER BY `c`.`Cursor`
        LIMIT @take;
        """;

    public async ValueTask<IInteractionTransaction> BeginSerializableAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            return BorrowedInteractionTransaction.Instance;
        return new InteractionTransaction(await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));
    }

    public async ValueTask<IInteractionTransaction> BeginReadCommittedAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            return BorrowedInteractionTransaction.Instance;
        return new InteractionTransaction(await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));
    }

    public async Task<InteractionAuthoritativeExportSnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken)
            : null;
        var highWatermark = await context.AuthorityCoordinates
            .AsNoTracking()
            .MaxAsync(item => (long?)item.Cursor, cancellationToken)
            ?? 0;
        var revisionHighWatermark = await context.AuthorityRevisions
            .AsNoTracking()
            .MaxAsync(item => (long?)item.RevisionCursor, cancellationToken)
            ?? 0;
        var partitions = await ReadInteractionPartitionManifestAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        var dbTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var minimumResourceVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `AppliedResourceVersion` FROM `InteractionAuthorityCurrentRows` FORCE INDEX (`IX_InteractionAuthorityCurrentRows_ResourceVersion`) ORDER BY `AppliedResourceVersion`, `ResourceType`, `ResourceId` LIMIT 1;",
            cancellationToken);
        var maximumResourceVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `AppliedResourceVersion` FROM `InteractionAuthorityCurrentRows` FORCE INDEX (`IX_InteractionAuthorityCurrentRows_ResourceVersion`) ORDER BY `AppliedResourceVersion` DESC, `ResourceType` DESC, `ResourceId` DESC LIMIT 1;",
            cancellationToken);
        var minimumSummaryVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `SummaryVersion` FROM `InteractionAuthorityCurrentRows` FORCE INDEX (`IX_InteractionAuthorityCurrentRows_SummaryVersion`) WHERE `SummaryVersion` IS NOT NULL ORDER BY `SummaryVersion`, `ResourceType`, `ResourceId` LIMIT 1;",
            cancellationToken);
        var maximumSummaryVersion = await ReadIndexedVersionBoundaryAsync(
            connection,
            dbTransaction,
            "SELECT `SummaryVersion` FROM `InteractionAuthorityCurrentRows` FORCE INDEX (`IX_InteractionAuthorityCurrentRows_SummaryVersion`) WHERE `SummaryVersion` IS NOT NULL ORDER BY `SummaryVersion` DESC, `ResourceType` DESC, `ResourceId` DESC LIMIT 1;",
            cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return new InteractionAuthoritativeExportSnapshot(
            highWatermark,
            revisionHighWatermark,
            DateTimeOffset.UtcNow,
            partitions.Sum(item => item.ItemCount),
            minimumResourceVersion,
            maximumResourceVersion,
            minimumSummaryVersion,
            maximumSummaryVersion,
            CombineXor(partitions.Select(item => item.XorSha256)));
    }

    public async Task<InteractionAuthoritativeExportSlice> ReadPageAsync(
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
        command.CommandText = AuthoritativePageSql;
        command.Parameters.Add(new MySqlParameter("@revisionHighWatermark", snapshotRevisionHighWatermark));
        command.Parameters.Add(new MySqlParameter("@afterCursor", afterCursor));
        command.Parameters.Add(new MySqlParameter("@highWatermark", snapshotHighWatermark));
        command.Parameters.Add(new MySqlParameter("@take", take + 1));
        var rows = new List<InteractionAuthoritativeExportRow>(take + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new InteractionAuthoritativeExportRow(
                reader.GetInt64(0),
                (ResourceType)reader.GetByte(1),
                reader.GetString(2),
                (ResourceLifecycle)reader.GetByte(3),
                (ResourceVisibility)reader.GetByte(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetBoolean(7),
                reader.GetInt64(8),
                Utc(reader.GetDateTime(9)),
                NullableInt64(reader, 10),
                NullableInt64(reader, 11),
                NullableInt64(reader, 12),
                NullableInt64(reader, 13),
                NullableInt64(reader, 14),
                NullableInt64(reader, 15),
                NullableInt64(reader, 16),
                NullableInt64(reader, 17),
                NullableInt64(reader, 18),
                NullableBoolean(reader, 19),
                NullableBoolean(reader, 20),
                NullableBoolean(reader, 21),
                NullableInt64(reader, 22),
                reader.IsDBNull(23) ? null : Utc(reader.GetDateTime(23)),
                reader.GetByte(24),
                reader.GetString(25)));
        }
        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        return new InteractionAuthoritativeExportSlice(rows, hasMore);
    }

    private static long? NullableInt64(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static bool? NullableBoolean(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<IReadOnlyList<InteractionAuthoritativeExportPartition>>
        ReadInteractionPartitionManifestAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            SELECT `PartitionKey`, `ItemCount`, `XorSha256`
            FROM `InteractionAuthorityPartitionManifests`
            WHERE `ItemCount` > 0
            ORDER BY `PartitionKey`;
            """;
        var result = new List<InteractionAuthoritativeExportPartition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new InteractionAuthoritativeExportPartition(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2).ToLowerInvariant()));
        }
        return result;
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

    public async Task<InteractionAuthorityRevisionRetentionState> GetRetentionStateAsync(
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
            "SELECT COALESCE(MAX(`TABLE_ROWS`), 0) FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'InteractionAuthoritativeStateRevisions';",
            cancellationToken);
        await using var sample = connection.CreateCommand();
        sample.Transaction = transaction;
        sample.CommandText =
            """
            SELECT `RecordedAtUtc`
            FROM `InteractionAuthorityRevisionPruneCandidates`
                FORCE INDEX (`IX_InteractionAuthorityRevisionPruneCandidates_Retention`)
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
                supersededAt.Add(Utc(reader.GetDateTime(0)));
        }
        return new InteractionAuthorityRevisionRetentionState(
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
                FROM `InteractionAuthorityRevisionPruneCandidates`
                    FORCE INDEX (`IX_InteractionAuthorityRevisionPruneCandidates_Retention`)
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
                enable.CommandText = "SET @interaction_authority_revision_prune = 1;";
                await enable.ExecuteNonQueryAsync(cancellationToken);
                bypassEnabled = true;
            }
            await using var prune = connection.CreateCommand();
            prune.Transaction = transaction;
            prune.CommandText = $"DELETE FROM `InteractionAuthoritativeStateRevisions` WHERE `RevisionCursor` IN ({Parameters(candidates.Count)});";
            AddCursorParameters(prune, candidates);
            var pruned = await prune.ExecuteNonQueryAsync(cancellationToken);
            await ResetInteractionPruneBypassAsync(connection, transaction);
            bypassEnabled = false;

            await using var removeClaims = connection.CreateCommand();
            removeClaims.Transaction = transaction;
            removeClaims.CommandText = $"DELETE FROM `InteractionAuthorityRevisionPruneCandidates` WHERE `RevisionCursor` IN ({Parameters(candidates.Count)});";
            AddCursorParameters(removeClaims, candidates);
            await removeClaims.ExecuteNonQueryAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return pruned;
        }
        finally
        {
            if (bypassEnabled)
                await ResetInteractionPruneBypassAsync(connection, transaction);
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

    private static async Task ResetInteractionPruneBypassAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction)
    {
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = "SET @interaction_authority_revision_prune = NULL;";
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await disable.ExecuteNonQueryAsync(cleanupTimeout.Token);
    }

    public Task<ResourceState?> GetResourceStateAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken) =>
        forUpdate
            ? context.ResourceStates.FromSqlInterpolated($"SELECT * FROM `ResourceStates` WHERE `ResourceType` = {(byte)resource.Type} AND `ResourceId` = {resource.Id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : context.ResourceStates.AsNoTracking().SingleOrDefaultAsync(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id, cancellationToken);

    public Task<bool> IsUserErasedAsync(long userId, CancellationToken cancellationToken) =>
        context.UserErasures.AsNoTracking().AnyAsync(item => item.UserId == userId, cancellationToken);

    public Task<ResourceStateGap?> GetResourceGapAsync(ResourceRef resource, long sourceVersion, CancellationToken cancellationToken) =>
        context.ResourceStateGaps.SingleOrDefaultAsync(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.SourceVersion == sourceVersion, cancellationToken);

    public async Task<ProjectionGapBacklogState> GetProjectionGapBacklogAsync(
        CancellationToken cancellationToken)
    {
        var aggregate = await context.ResourceStateGaps
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new ProjectionGapBacklogState(
                group.LongCount(),
                group.Min(item => item.ReceivedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken);
        return aggregate ?? new ProjectionGapBacklogState(0, null);
    }

    public void AddResourceState(ResourceState state) => context.ResourceStates.Add(state);
    public void AddResourceGap(ResourceStateGap gap) => context.ResourceStateGaps.Add(gap);
    public void RemoveResourceGap(ResourceStateGap gap) => context.ResourceStateGaps.Remove(gap);

    public Task<Reaction?> GetReactionAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken) =>
        forUpdate
            ? context.Reactions.FromSqlInterpolated($"SELECT * FROM `Reactions` WHERE `ResourceType` = {(byte)resource.Type} AND `ResourceId` = {resource.Id} AND `ActorId` = {actorId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : context.Reactions.AsNoTracking().SingleOrDefaultAsync(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.ActorId == actorId, cancellationToken);

    public void AddReaction(Reaction reaction) => context.Reactions.Add(reaction);
    public void RemoveReaction(Reaction reaction) => context.Reactions.Remove(reaction);

    public Task<Rating?> GetRatingAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken) =>
        forUpdate
            ? context.Ratings.FromSqlInterpolated($"SELECT * FROM `Ratings` WHERE `ResourceType` = {(byte)resource.Type} AND `ResourceId` = {resource.Id} AND `ActorId` = {actorId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : context.Ratings.AsNoTracking().SingleOrDefaultAsync(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.ActorId == actorId, cancellationToken);

    public void AddRating(Rating rating) => context.Ratings.Add(rating);
    public void RemoveRating(Rating rating) => context.Ratings.Remove(rating);

    public Task<EngagementSummary?> GetSummaryAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken) =>
        forUpdate
            ? context.EngagementSummaries.FromSqlInterpolated($"SELECT * FROM `EngagementSummaries` WHERE `ResourceType` = {(byte)resource.Type} AND `ResourceId` = {resource.Id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : context.EngagementSummaries.AsNoTracking().SingleOrDefaultAsync(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id, cancellationToken);

    public void AddSummary(EngagementSummary summary) => context.EngagementSummaries.Add(summary);

    public async Task<PublicSummaryState> GetPublicSummaryStateAsync(ResourceRef resource, CancellationToken cancellationToken)
    {
        var state = await context.ResourceStates.AsNoTracking().SingleOrDefaultAsync(
            x => x.ResourceType == resource.Type && x.ResourceId == resource.Id, cancellationToken);
        if (state?.IsPubliclyInteractable != true)
            return new PublicSummaryState(state, null);
        var summary = await context.EngagementSummaries.AsNoTracking().SingleOrDefaultAsync(
            x => x.ResourceType == resource.Type && x.ResourceId == resource.Id, cancellationToken);
        return new PublicSummaryState(state, summary);
    }

    public async Task<bool> InsertViewObservationAsync(ViewObservation value, CancellationToken cancellationToken)
    {
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO `ViewObservations`
(`ResourceType`,`ResourceId`,`ObserverHash`,`BucketStartUtc`,`ReceivedAtUtc`,`ExpiresAtUtc`,`ProcessedAtUtc`)
VALUES ({(byte)value.ResourceType},{value.ResourceId},{value.ObserverHash},{value.BucketStartUtc},{value.ReceivedAtUtc},{value.ExpiresAtUtc},NULL)
ON DUPLICATE KEY UPDATE `Id` = `Id`;", cancellationToken);
        return rows == 1;
    }

    public async Task<IReadOnlyList<ViewObservation>> LockPendingViewsAsync(
        DateTimeOffset notExpiredAtUtc,
        int maximum,
        CancellationToken cancellationToken) =>
        await context.ViewObservations.FromSqlInterpolated($@"
SELECT * FROM `ViewObservations`
WHERE `ProcessedAtUtc` IS NULL AND `ExpiresAtUtc` >= {notExpiredAtUtc}
ORDER BY `Id`
LIMIT {maximum}
FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EngagementSummary>> LockSummariesForReconciliationAsync(int maximum, CancellationToken cancellationToken) =>
        await context.EngagementSummaries.FromSqlInterpolated($@"
SELECT * FROM `EngagementSummaries`
ORDER BY `LastReconciledAtUtc`,`ResourceType`,`ResourceId`
LIMIT {maximum}
FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken);

    public async Task<(long Likes, long Dislikes)> CountReactionsAsync(ResourceRef resource, CancellationToken cancellationToken)
    {
        var query = context.Reactions.AsNoTracking().Where(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id);
        var likes = await query.LongCountAsync(x => x.Kind == ReactionKind.Like, cancellationToken);
        var dislikes = await query.LongCountAsync(x => x.Kind == ReactionKind.Dislike, cancellationToken);
        return (likes, dislikes);
    }

    public async Task<(long Count, long Total)> AggregateRatingsAsync(ResourceRef resource, CancellationToken cancellationToken)
    {
        var query = context.Ratings.AsNoTracking().Where(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id);
        var count = await query.LongCountAsync(cancellationToken);
        var total = count == 0 ? 0 : await query.SumAsync(x => (long)x.Score, cancellationToken);
        return (count, total);
    }

    public async Task<IReadOnlyList<ViewerInteractionState>> GetViewerStatusesAsync(
        long actorId,
        IReadOnlyList<ResourceRef> resources,
        CancellationToken cancellationToken)
    {
        var ids = resources.Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();
        var states = await context.ResourceStates.AsNoTracking().Where(x => ids.Contains(x.ResourceId)).ToListAsync(cancellationToken);
        var erased = await IsUserErasedAsync(actorId, cancellationToken);
        var reactions = erased
            ? []
            : await context.Reactions.AsNoTracking().Where(x => x.ActorId == actorId && ids.Contains(x.ResourceId)).ToListAsync(cancellationToken);
        var ratings = erased
            ? []
            : await context.Ratings.AsNoTracking().Where(x => x.ActorId == actorId && ids.Contains(x.ResourceId)).ToListAsync(cancellationToken);
        return resources.Select(resource =>
        {
            var available = states.Any(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.IsPubliclyInteractable);
            var reaction = available ? reactions.SingleOrDefault(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id) : null;
            var rating = available ? ratings.SingleOrDefault(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id) : null;
            return new ViewerInteractionState(
                resource, available, reaction?.Kind, rating?.Score,
                reaction?.Version, rating?.Version);
        }).ToArray();
    }

    public Task<int> DeleteExpiredViewObservationsAsync(DateTimeOffset now, int maximum, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM `ViewObservations` WHERE `ExpiresAtUtc` < {now} ORDER BY `ExpiresAtUtc`, `Id` LIMIT {maximum}",
            cancellationToken);

    public async Task<int> PurgeResourcePersonalDataAsync(
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        var resourceType = resource.Type;
        var resourceId = resource.Id;
        var removed = 0;
        removed += await context.Reactions
            .Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId)
            .ExecuteDeleteAsync(cancellationToken);
        removed += await context.Ratings
            .Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId)
            .ExecuteDeleteAsync(cancellationToken);
        removed += await context.ViewObservations
            .Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId)
            .ExecuteDeleteAsync(cancellationToken);
        return removed;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InteractionConflictException("The interaction changed concurrently; retry with the latest state.", exception);
        }
        catch (DbUpdateException exception) when (FindMySqlException(exception)?.Number == 1062)
        {
            throw new InteractionConflictException("A concurrent request already created this interaction.", exception);
        }
    }

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is MySqlException mysql)
                return mysql;
        return null;
    }

    private sealed class InteractionTransaction(IDbContextTransaction transaction) : IInteractionTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class BorrowedInteractionTransaction : IInteractionTransaction
    {
        internal static BorrowedInteractionTransaction Instance { get; } = new();
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
