using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class FollowingReadRepository(FollowingContext context) : IFollowingReadRepository
    {
        public async Task<IReadOnlyList<CategoryFollow>> FindCategoryFollows(
            int userId,
            IReadOnlyCollection<int> categoryIds,
            IReadOnlyCollection<string> categoryNames,
            CancellationToken cancellationToken)
        {
            var ids = categoryIds?.ToArray() ?? Array.Empty<int>();
            var names = categoryNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
                ?? Array.Empty<string>();

            if (ids.Length == 0 && names.Length == 0)
                return Array.Empty<CategoryFollow>();

            return await context.CategoryFollows.AsNoTracking()
                .Where(item => item.UserId == userId &&
                    (ids.Contains(item.CategoryId) || names.Contains(item.CategoryName)))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyCollection<int>> FindFollowedUserIds(
            int userId,
            IReadOnlyCollection<int> followedUserIds,
            CancellationToken cancellationToken)
        {
            var ids = followedUserIds?.ToArray() ?? Array.Empty<int>();
            if (ids.Length == 0)
                return Array.Empty<int>();

            return await context.Follows.AsNoTracking()
                .Where(item => item.FollowerUserId == userId && ids.Contains(item.FollowedUserId))
                .Select(item => item.FollowedUserId)
                .ToArrayAsync(cancellationToken);
        }

        public async Task<RelationsSnapshot> GetRelations(
            int userId,
            int otherUserId,
            CancellationToken cancellationToken)
        {
            var isFriend = await context.Friendships.AsNoTracking().AnyAsync(item =>
                item.UserId == userId && item.FriendUserId == otherUserId,
                cancellationToken);
            var isFriendRequestSent = await context.FriendRequests.AsNoTracking().AnyAsync(item =>
                item.Status == FriendRequestStatus.Pending &&
                ((item.SenderUserId == userId && item.RecipientUserId == otherUserId) ||
                 (item.SenderUserId == otherUserId && item.RecipientUserId == userId)),
                cancellationToken);
            var isFollowing = await context.Follows.AsNoTracking().AnyAsync(item =>
                item.FollowerUserId == userId && item.FollowedUserId == otherUserId,
                cancellationToken);
            var blockDirections = await context.UserBlocks.AsNoTracking()
                .Where(item =>
                    (item.BlockerUserId == userId && item.BlockedUserId == otherUserId)
                    || (item.BlockerUserId == otherUserId && item.BlockedUserId == userId))
                .Select(item => new { item.BlockerUserId, item.BlockedUserId })
                .ToArrayAsync(cancellationToken);

            return new RelationsSnapshot(
                isFriend,
                isFriendRequestSent,
                isFollowing,
                blockDirections.Any(item => item.BlockerUserId == userId),
                blockDirections.Any(item => item.BlockerUserId == otherUserId));
        }

        public async Task<UserRelationAnalytics> GetUserAnalytics(
            int userId,
            CancellationToken cancellationToken)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));

            // One set-based aggregate over the bounded 64 shards; no shard entities are tracked.
            var totals = await context.UserRelationsAnalytics
                .AsNoTracking()
                .Where(item => item.Id == userId)
                .GroupBy(_ => 1)
                .Select(group => new AnalyticsTotals(
                    group.Sum(item => item.TotalFriends),
                    group.Sum(item => item.TotalFollowers),
                    group.Sum(item => item.TotalFollowing),
                    group.Sum(item => item.TotalCategoriesFollowed),
                    group.Sum(item => item.TotalFriendsRequests)))
                .SingleOrDefaultAsync(cancellationToken);

            return totals is null
                ? new UserRelationAnalytics(userId)
                : UserRelationAnalytics.Aggregate(
                    userId,
                    totals.TotalFriends,
                    totals.TotalFollowers,
                    totals.TotalFollowing,
                    totals.TotalCategoriesFollowed,
                    totals.TotalFriendsRequests);
        }

        public async Task<FollowingCursorSlice<FriendRequestCursorReadModel>> ListFriendRequestsCursor(
            int currentUserId,
            bool sentRequests,
            FriendRequestStatus status,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var rows = await FollowingCursorQueries.FriendRequests(
                    context.FriendRequests.AsNoTracking(),
                    currentUserId,
                    sentRequests,
                    status,
                    window)
                .ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

        public async Task<FollowingCursorSlice<FriendshipCursorReadModel>> ListFriendsCursor(
            int ownerUserId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var rows = await FollowingCursorQueries.Friends(
                    context.Friendships.AsNoTracking(),
                    ownerUserId,
                    window)
                .ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

        public async Task<FollowingCursorSlice<FollowCursorReadModel>> ListFollowsCursor(
            int userId,
            bool following,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var rows = await FollowingCursorQueries.Follows(
                    context.Follows.AsNoTracking(),
                    userId,
                    following,
                    window)
                .ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

        public async Task<FollowingCursorSlice<CategoryFollowCursorReadModel>> ListCategoryFollowsCursor(
            int userId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var rows = await FollowingCursorQueries.CategoryFollows(
                    context.CategoryFollows.AsNoTracking(),
                    userId,
                    window)
                .ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

        public async Task<FollowingCursorSlice<UserBlockCursorReadModel>> ListUserBlocksCursor(
            int blockerUserId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var rows = await FollowingCursorQueries.UserBlocks(
                    context.UserBlocks.AsNoTracking(),
                    blockerUserId,
                    window)
                .ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

        public async Task<FollowingCursorSlice<RelationIdCursorReadModel>> ListRelationIdsCursor(
            int userId,
            RelationIdKind kind,
            FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            var query = kind switch
            {
                RelationIdKind.Friends => FollowingCursorQueries.FriendRelationIds(
                    context.Friendships.AsNoTracking(), userId, window),
                RelationIdKind.Followers => FollowingCursorQueries.FollowerRelationIds(
                    context.Follows.AsNoTracking(), userId, window),
                RelationIdKind.Categories => FollowingCursorQueries.CategoryRelationIds(
                    context.CategoryFollows.AsNoTracking(), userId, window),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            var rows = await query.ToListAsync(cancellationToken);
            return FollowingCursorQueries.ToSlice(rows, window.PageSize);
        }

    }

    internal sealed record AnalyticsTotals(
        int TotalFriends,
        int TotalFollowers,
        int TotalFollowing,
        int TotalCategoriesFollowed,
        int TotalFriendsRequests);
}
