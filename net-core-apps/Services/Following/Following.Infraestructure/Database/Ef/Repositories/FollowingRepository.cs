using Following.Domain.Repositories;
using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class FollowingRepository(FollowingContext context) : IFollowingRepository
    {
        public async Task<IRelationshipPairMutation> AcquireRelationshipPairMutation(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken)
        {
            if (firstUserId <= 0 || secondUserId <= 0 || firstUserId == secondUserId)
                throw new ArgumentOutOfRangeException(nameof(firstUserId));
            if (context.Database.CurrentTransaction is not null)
            {
                throw new InvalidOperationException(
                    "A relationship pair mutation is already active on this unit of work.");
            }

            var pairMinUserId = Math.Min(firstUserId, secondUserId);
            var pairMaxUserId = Math.Max(firstUserId, secondUserId);
            var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                await AcquireUserAuthorityLocksAsync(
                    transaction,
                    [pairMinUserId, pairMaxUserId],
                    exclusive: false,
                    cancellationToken);
                // The no-op duplicate update intentionally acquires an exclusive InnoDB record
                // lock which is held until commit/rollback. Every relationship mutation uses the
                // same normalized row, closing block-vs-create races across separate tables.
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO `RelationshipPairGuard`
                        (`PairMinUserId`, `PairMaxUserId`, `CreatedAtUtc`)
                    VALUES ({pairMinUserId}, {pairMaxUserId}, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE
                        `PairMinUserId` = `PairMinUserId`;
                    """,
                    cancellationToken);

                return new RelationshipPairMutation(transaction);
            }
            catch
            {
                await RollbackAndDisposeAsync(transaction);
                throw;
            }
        }

        public Task<IUserMutationScope> AcquireUserMutation(
            int userId,
            CancellationToken cancellationToken) =>
            AcquireUserMutationCore(userId, exclusive: false, cancellationToken);

        public Task<IUserMutationScope> AcquireUserErasureMutation(
            int userId,
            CancellationToken cancellationToken) =>
            AcquireUserMutationCore(userId, exclusive: true, cancellationToken);

        public async Task EnsureUsersNotErased(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            var normalized = userIds.Distinct().ToArray();
            if (normalized.Length is 0 or > 16 || normalized.Any(userId => userId <= 0))
                throw new ArgumentOutOfRangeException(nameof(userIds));

            var erased = await context.UserErasures
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(item => normalized.Contains(item.UserId), cancellationToken);
            if (erased)
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "A relationship participant is no longer available.",
                    410);
            }
        }

        public Task<UserBlock?> GetBlock(
            int blockerUserId,
            int blockedUserId,
            CancellationToken cancellationToken) =>
            context.UserBlocks.FirstOrDefaultAsync(item =>
                item.BlockerUserId == blockerUserId
                && item.BlockedUserId == blockedUserId,
                cancellationToken);

        public Task<bool> IsBlockedEitherDirection(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken) =>
            context.UserBlocks.AsNoTracking().AnyAsync(item =>
                (item.BlockerUserId == firstUserId && item.BlockedUserId == secondUserId)
                || (item.BlockerUserId == secondUserId && item.BlockedUserId == firstUserId),
                cancellationToken);

        public Task<CategoryFollow?> GetCategoryFollow(
            int userId,
            int? categoryId,
            string? categoryName,
            CancellationToken cancellationToken)
        {
            var query = context.CategoryFollows.Where(item => item.UserId == userId);
            return categoryId is > 0
                ? query.FirstOrDefaultAsync(item => item.CategoryId == categoryId.Value, cancellationToken)
                : query.FirstOrDefaultAsync(item => item.CategoryName == categoryName, cancellationToken);
        }

        public Task<Follow?> GetFollow(int userId, int followedId, CancellationToken cancellationToken) =>
            context.Follows.FirstOrDefaultAsync(item =>
                item.FollowerUserId == userId && item.FollowedUserId == followedId,
                cancellationToken);

        public Task<FriendRequest?> GetFriendRequest(int userId, int friendId, CancellationToken cancellationToken) =>
            context.FriendRequests.FirstOrDefaultAsync(item =>
                item.Status == FriendRequestStatus.Pending &&
                ((item.SenderUserId == userId && item.RecipientUserId == friendId) ||
                 (item.SenderUserId == friendId && item.RecipientUserId == userId)),
                cancellationToken);

        public Task<FriendRequest?> GetFriendRequestForApproval(
            int requestId,
            int friendId,
            CancellationToken cancellationToken) =>
            context.FriendRequests.FirstOrDefaultAsync(item =>
                item.Id == requestId &&
                item.RecipientUserId == friendId &&
                item.Status == FriendRequestStatus.Pending,
                cancellationToken);

        public Task<bool> IsFriend(int userId, int friendId, CancellationToken cancellationToken) =>
            context.Friendships.AsNoTracking().AnyAsync(item =>
                item.UserId == userId && item.FriendUserId == friendId,
                cancellationToken);

        public async Task<IReadOnlyList<Friendship>> GetFriendships(
            int userId,
            int friendId,
            CancellationToken cancellationToken) =>
            await context.Friendships
                .Where(item =>
                    (item.UserId == userId && item.FriendUserId == friendId) ||
                    (item.UserId == friendId && item.FriendUserId == userId))
                .ToListAsync(cancellationToken);

        public Task<FriendshipSubscriptionFeature?> GetSubscriptionFeature(
            string subscriptionId,
            int requesterId,
            int ownerId,
            CancellationToken cancellationToken) =>
            context.FriendsSubscriptionsFeature.FirstOrDefaultAsync(item =>
                item.SubscriptionId == subscriptionId &&
                item.RequesterId == requesterId &&
                item.OwnerId == ownerId,
                cancellationToken);

        public async Task<UserRelationAnalytics> GetOrCreateAnalytics(
            int userId,
            int shardDiscriminator,
            CancellationToken cancellationToken)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            var shardId = UserRelationAnalytics.ResolveShard(shardDiscriminator);

            // Creating an empty shard is idempotent and happens before it is tracked. This avoids
            // duplicate-key races without retrying a celebrity's hot aggregate row.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO `UserRelationAnalytics`
                    (`Id`, `ShardId`, `TotalFriends`, `TotalFollowers`, `TotalFollowing`,
                     `TotalCategoriesFollowed`, `TotalFriendsRequests`, `CreatedDate`,
                     `UpdateDate`, `UserUpdate`, `Version`)
                VALUES
                    ({userId}, {shardId}, 0, 0, 0, 0, 0, UTC_TIMESTAMP(6),
                     UTC_TIMESTAMP(6), 'SISTEMA', 1)
                ON DUPLICATE KEY UPDATE `Id` = `Id`;
                """,
                cancellationToken);

            var analytics = await context.UserRelationsAnalytics
                .SingleAsync(
                    item => item.Id == userId && item.ShardId == shardId,
                    cancellationToken);

            return analytics;
        }

        public async Task Add<T>(T entity, CancellationToken cancellationToken) where T : class =>
            await context.Set<T>().AddAsync(entity, cancellationToken);

        public async Task AddRange<T>(IEnumerable<T> entities, CancellationToken cancellationToken) where T : class =>
            await context.Set<T>().AddRangeAsync(entities, cancellationToken);

        public void Remove<T>(T entity) where T : class => context.Remove(entity);

        public void RemoveRange<T>(IEnumerable<T> entities) where T : class =>
            context.RemoveRange(entities);

        public async Task SaveChanges(CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "The relationship changed concurrently. Reload it and retry.",
                    409);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is MySqlConnector.MySqlException { Number: 1062 })
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "The requested relationship already exists.",
                    409);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is MySqlConnector.MySqlException { Number: 1644 } mysql
                    && mysql.Message.Contains("erased", StringComparison.OrdinalIgnoreCase))
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "A relationship participant is no longer available.",
                    410);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is MySqlConnector.MySqlException { Number: 1644 })
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "A blocked user relationship cannot be created.",
                    409);
            }
        }

        private async Task<IUserMutationScope> AcquireUserMutationCore(
            int userId,
            bool exclusive,
            CancellationToken cancellationToken)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("A user mutation is already active on this unit of work.");

            var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            try
            {
                await AcquireUserAuthorityLocksAsync(
                    transaction,
                    [userId],
                    exclusive,
                    cancellationToken);
                return new UserMutationScope(transaction);
            }
            catch
            {
                await RollbackAndDisposeAsync(transaction);
                throw;
            }
        }

        private async Task AcquireUserAuthorityLocksAsync(
            IDbContextTransaction transaction,
            IReadOnlyCollection<int> userIds,
            bool exclusive,
            CancellationToken cancellationToken)
        {
            foreach (var userId in userIds.OrderBy(value => value))
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT IGNORE INTO `UserMutationGuard` (`UserId`, `CreatedAtUtc`)
                    VALUES ({userId}, UTC_TIMESTAMP(6));
                    """,
                    cancellationToken);

                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = exclusive
                    ? "SELECT `UserId` FROM `UserMutationGuard` WHERE `UserId` = @userId FOR UPDATE;"
                    : "SELECT `UserId` FROM `UserMutationGuard` WHERE `UserId` = @userId LOCK IN SHARE MODE;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@userId";
                parameter.Value = userId;
                command.Parameters.Add(parameter);
                var lockedUser = await command.ExecuteScalarAsync(cancellationToken);
                if (lockedUser is null or DBNull)
                    throw new InvalidOperationException("The user mutation authority row was not acquired.");
            }
        }

        private static async Task RollbackAndDisposeAsync(IDbContextTransaction transaction)
        {
            try
            {
                using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await transaction.RollbackAsync(rollbackBudget.Token);
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }

        private sealed class RelationshipPairMutation(IDbContextTransaction transaction)
            : IRelationshipPairMutation
        {
            private bool _completed;

            public async Task CommitAsync(CancellationToken cancellationToken)
            {
                if (_completed)
                    throw new InvalidOperationException("The relationship mutation is already complete.");

                await transaction.CommitAsync(cancellationToken);
                _completed = true;
            }

            public async ValueTask DisposeAsync()
            {
                if (!_completed)
                    await RollbackAndDisposeAsync(transaction);
                else
                    await transaction.DisposeAsync();

                _completed = true;
            }
        }

        private sealed class UserMutationScope(IDbContextTransaction transaction)
            : IUserMutationScope
        {
            private bool _completed;

            public async Task CommitAsync(CancellationToken cancellationToken)
            {
                if (_completed)
                    throw new InvalidOperationException("The user mutation is already complete.");
                await transaction.CommitAsync(cancellationToken);
                _completed = true;
            }

            public async ValueTask DisposeAsync()
            {
                if (!_completed)
                    await RollbackAndDisposeAsync(transaction);
                else
                    await transaction.DisposeAsync();
                _completed = true;
            }
        }
    }
}
