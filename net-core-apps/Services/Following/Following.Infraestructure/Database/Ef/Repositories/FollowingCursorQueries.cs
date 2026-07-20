using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Following.Infra.Persistence
{
    internal static class FollowingCursorQueries
    {
        internal static IQueryable<FriendRequestCursorReadModel> FriendRequests(
            IQueryable<FriendRequest> source,
            int currentUserId,
            bool sentRequests,
            FriendRequestStatus status,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            var query = sentRequests
                ? source.Where(item => item.SenderUserId == currentUserId)
                : source.Where(item => item.RecipientUserId == currentUserId);
            query = query.Where(item => item.Status == status);
            query = After(query, window.After);

            return query
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new FriendRequestCursorReadModel(
                    item.Id,
                    item.SenderUserId,
                    item.SenderDisplayName,
                    item.RecipientUserId,
                    item.RecipientDisplayName,
                    item.Status,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<FriendshipCursorReadModel> Friends(
            IQueryable<Friendship> source,
            int ownerUserId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.UserId == ownerUserId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new FriendshipCursorReadModel(
                    item.Id,
                    item.UserId,
                    item.FriendUserId,
                    item.FriendDisplayName,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<FollowCursorReadModel> Follows(
            IQueryable<Follow> source,
            int userId,
            bool following,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            var query = following
                ? source.Where(item => item.FollowerUserId == userId)
                : source.Where(item => item.FollowedUserId == userId);

            return After(query, window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new FollowCursorReadModel(
                    item.Id,
                    item.FollowerUserId,
                    item.FollowerDisplayName,
                    item.FollowedUserId,
                    item.FollowedDisplayName,
                    item.IsFollowedBack,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<CategoryFollowCursorReadModel> CategoryFollows(
            IQueryable<CategoryFollow> source,
            int userId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.UserId == userId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new CategoryFollowCursorReadModel(
                    item.Id,
                    item.UserId,
                    item.CategoryId,
                    item.CategoryName,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<UserBlockCursorReadModel> UserBlocks(
            IQueryable<UserBlock> source,
            int blockerUserId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.BlockerUserId == blockerUserId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new UserBlockCursorReadModel(
                    item.Id,
                    item.BlockedUserId,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<RelationIdCursorReadModel> FriendRelationIds(
            IQueryable<Friendship> source,
            int userId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.UserId == userId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new RelationIdCursorReadModel(
                    item.Id,
                    item.FriendUserId,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<RelationIdCursorReadModel> FollowerRelationIds(
            IQueryable<Follow> source,
            int userId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.FollowedUserId == userId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new RelationIdCursorReadModel(
                    item.Id,
                    item.FollowerUserId,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static IQueryable<RelationIdCursorReadModel> CategoryRelationIds(
            IQueryable<CategoryFollow> source,
            int userId,
            FollowingCursorWindow window)
        {
            ValidateWindow(window);
            return After(
                    source.Where(item => item.UserId == userId),
                    window.After)
                .OrderByDescending(item => item.CreatedDate)
                .ThenByDescending(item => item.Id)
                .Select(item => new RelationIdCursorReadModel(
                    item.Id,
                    item.CategoryId,
                    item.CreatedDate))
                .Take(window.PageSize + 1);
        }

        internal static FollowingCursorSlice<T> ToSlice<T>(
            IReadOnlyList<T> rows,
            int pageSize)
            where T : IFollowingCursorReadModel
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (pageSize is < 1 or > FollowingReadLimits.MaximumCursorPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            var hasMore = rows.Count > pageSize;
            var items = hasMore
                ? rows.Take(pageSize).ToArray()
                : rows.ToArray();
            var next = hasMore
                ? new FollowingCursorPosition(
                    NormalizeUtc(items[^1].CreatedDateUtc),
                    items[^1].RowId)
                : null;
            return new FollowingCursorSlice<T>(items, next, hasMore);
        }

        private static IQueryable<T> After<T>(
            IQueryable<T> query,
            FollowingCursorPosition? position)
            where T : FollowingEntity
        {
            if (position is null)
                return query;

            var createdDate = NormalizeUtc(position.CreatedDateUtc);
            return query.Where(item =>
                item.CreatedDate < createdDate ||
                (item.CreatedDate == createdDate && item.Id < position.Id));
        }

        private static void ValidateWindow(FollowingCursorWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (window.PageSize is < 1 or > FollowingReadLimits.MaximumCursorPageSize)
                throw new ArgumentOutOfRangeException(nameof(window));
            if (window.After is { Id: <= 0 })
                throw new ArgumentOutOfRangeException(nameof(window));
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
