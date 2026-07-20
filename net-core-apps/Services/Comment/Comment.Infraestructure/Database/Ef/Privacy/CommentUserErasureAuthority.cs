using Comment.Application.Features.Privacy;
using Comment.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Privacy;

public sealed class CommentUserErasureAuthority(CommentContext context)
    : ICommentUserErasureAuthority
{
    public Task<ICommentUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) =>
        AcquireAsync(userIds, exclusive: false, cancellationToken);

    internal Task<ICommentUserMutationScope> AcquireErasureMutationAsync(
        int userId,
        CancellationToken cancellationToken) =>
        AcquireAsync([userId], exclusive: true, cancellationToken);

    public Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        return context.CommentUserErasures.AsNoTracking()
            .AnyAsync(value => value.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(userIds);
        if (normalized.Length == 0)
            return new HashSet<int>();
        return (await context.CommentUserErasures.AsNoTracking()
                .Where(value => normalized.Contains(value.UserId))
                .Select(value => value.UserId)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<ICommentUserMutationScope> AcquireAsync(
        IReadOnlyCollection<int> userIds,
        bool exclusive,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(userIds);
        if (normalized.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(userIds));
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A Comment user mutation is already active in this unit of work.");

        var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            foreach (var userId in normalized)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT IGNORE INTO `CommentUserMutationGuard` (`UserId`, `CreatedAtUtc`)
                    VALUES ({userId}, UTC_TIMESTAMP(6));
                    """, cancellationToken);

                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = exclusive
                    ? "SELECT `UserId` FROM `CommentUserMutationGuard` WHERE `UserId` = @userId FOR UPDATE;"
                    : "SELECT `UserId` FROM `CommentUserMutationGuard` WHERE `UserId` = @userId LOCK IN SHARE MODE;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@userId";
                parameter.Value = userId;
                command.Parameters.Add(parameter);
                if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull)
                    throw new InvalidOperationException("The Comment user mutation authority row was not acquired.");
            }

            var erased = await context.CommentUserErasures.AsNoTracking()
                .AnyAsync(value => normalized.Contains(value.UserId), cancellationToken);
            return new Scope(transaction, allowed: exclusive || !erased);
        }
        catch
        {
            await RollbackAndDisposeAsync(transaction);
            throw;
        }
    }

    private static int[] Normalize(IReadOnlyCollection<int> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var normalized = userIds.Distinct().OrderBy(value => value).ToArray();
        if (normalized.Length > 1_000 || normalized.Any(value => value <= 0))
            throw new ArgumentOutOfRangeException(nameof(userIds),
                "At most 1,000 positive Comment mutation subjects are allowed.");
        return normalized;
    }

    private static async Task RollbackAndDisposeAsync(IDbContextTransaction transaction)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(timeout.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private sealed class Scope(IDbContextTransaction transaction, bool allowed)
        : ICommentUserMutationScope
    {
        private bool _finished;
        public bool Allowed { get; } = allowed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_finished)
                throw new InvalidOperationException("The Comment mutation scope is complete.");
            await transaction.CommitAsync(cancellationToken);
            _finished = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_finished)
                await RollbackAndDisposeAsync(transaction);
            else
                await transaction.DisposeAsync();
            _finished = true;
        }
    }
}
