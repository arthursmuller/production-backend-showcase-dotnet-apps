using Feed.Application.Features.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence;

public sealed class FeedUserErasureAuthority(FeedContext context) : IFeedUserErasureAuthority
{
    private const int MaximumSubjectsPerMutation = 1_024;

    public Task<IFeedUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) => AcquireAsync(userIds, cancellationToken);

    internal Task<IFeedUserMutationScope> AcquireErasureMutationAsync(
        int userId,
        CancellationToken cancellationToken) => AcquireAsync([userId], cancellationToken);

    public Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        return context.FeedUserErasures.AsNoTracking()
            .AnyAsync(item => item.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(userIds);
        if (normalized.Length == 0)
            return new HashSet<int>();
        return (await context.FeedUserErasures.AsNoTracking()
                .Where(item => normalized.Contains(item.UserId))
                .Select(item => item.UserId)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<IFeedUserMutationScope> AcquireAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(userIds);
        if (normalized.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(userIds));

        var ownsTransaction = context.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : context.Database.CurrentTransaction
                ?? throw new InvalidOperationException(
                    "The existing Feed user-mutation transaction was lost.");
        try
        {
            foreach (var userId in normalized)
            {
                // A single no-op upsert acquires and retains the exclusive record lock.
                // INSERT IGNORE followed by SELECT FOR UPDATE allows concurrent duplicate
                // inserters to hold shared locks and deadlock while upgrading them.
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO `FeedUserMutationGuard` (`UserId`, `CreatedAtUtc`)
                    VALUES ({userId}, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE `UserId` = `UserId`;
                    """, cancellationToken);
            }

            var erased = (await context.FeedUserErasures.AsNoTracking()
                    .Where(item => normalized.Contains(item.UserId))
                    .Select(item => item.UserId)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet();
            return new MutationScope(transaction, ownsTransaction, erased);
        }
        catch
        {
            if (ownsTransaction)
                await RollbackAndDisposeAsync(transaction);
            throw;
        }
    }

    private static int[] Normalize(IReadOnlyCollection<int> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var normalized = userIds.Distinct().OrderBy(value => value).ToArray();
        if (normalized.Length > MaximumSubjectsPerMutation || normalized.Any(value => value <= 0))
            throw new ArgumentOutOfRangeException(nameof(userIds));
        return normalized;
    }

    private static async Task RollbackAndDisposeAsync(IDbContextTransaction transaction)
    {
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(budget.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private sealed class MutationScope(
        IDbContextTransaction transaction,
        bool ownsTransaction,
        IReadOnlySet<int> erasedUserIds) : IFeedUserMutationScope
    {
        private bool _completed;
        public bool Allowed => ErasedUserIds.Count == 0;
        public IReadOnlySet<int> ErasedUserIds { get; } = erasedUserIds;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_completed)
                return;
            if (ownsTransaction)
                await transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (ownsTransaction)
            {
                if (!_completed)
                    await RollbackAndDisposeAsync(transaction);
                else
                    await transaction.DisposeAsync();
            }
            _completed = true;
        }
    }
}
