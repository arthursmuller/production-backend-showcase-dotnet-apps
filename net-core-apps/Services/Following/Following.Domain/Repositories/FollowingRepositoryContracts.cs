using Following.Domain.AggregatesModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Domain.Repositories
{
    public static class FollowingReadLimits
    {
        public const int MaximumCursorPageSize = 100;
    }

    public enum RelationIdKind
    {
        Friends = 1,
        Followers = 2,
        Categories = 3,
    }

    public sealed record FollowingCursorPosition(
        DateTime CreatedDateUtc,
        int Id);

    public sealed record FollowingCursorWindow(
        int PageSize,
        FollowingCursorPosition? After);

    public interface IFollowingCursorReadModel
    {
        int RowId { get; }
        DateTime CreatedDateUtc { get; }
    }

    public sealed record FollowingCursorSlice<T>(
        IReadOnlyList<T> Items,
        FollowingCursorPosition? NextPosition,
        bool HasMore)
        where T : IFollowingCursorReadModel;

    public sealed record FriendRequestCursorReadModel(
        int RowId,
        int SenderUserId,
        string? SenderDisplayName,
        int RecipientUserId,
        string? RecipientDisplayName,
        FriendRequestStatus Status,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    public sealed record FriendshipCursorReadModel(
        int RowId,
        int UserId,
        int FriendUserId,
        string? FriendDisplayName,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    public sealed record FollowCursorReadModel(
        int RowId,
        int FollowerUserId,
        string? FollowerDisplayName,
        int FollowedUserId,
        string? FollowedDisplayName,
        bool IsFollowedBack,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    public sealed record CategoryFollowCursorReadModel(
        int RowId,
        int UserId,
        int CategoryId,
        string? CategoryName,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    public sealed record RelationIdCursorReadModel(
        int RowId,
        int RelationId,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    public sealed record UserBlockCursorReadModel(
        int RowId,
        int BlockedUserId,
        DateTime CreatedDateUtc) : IFollowingCursorReadModel;

    /// <summary>
    /// A transaction-scoped, normalized user-pair mutex. Every user-to-user mutation takes this
    /// lock before reading block or relationship state so cross-table writes are serialized for
    /// one pair while unrelated pairs remain fully concurrent.
    /// </summary>
    public interface IRelationshipPairMutation : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Transaction-scoped shared/exclusive user authority. Ordinary mutations take a shared
    /// lease; erasure scheduling takes the exclusive form so the durable tombstone is ordered
    /// after every in-flight mutation without serializing unrelated concurrent relationships.
    /// </summary>
    public interface IUserMutationScope : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken);
    }

    public sealed class NoopUserMutationScope : IUserMutationScope
    {
        public static NoopUserMutationScope Instance { get; } = new();
        private NoopUserMutationScope() { }
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class NoopRelationshipPairMutation : IRelationshipPairMutation
    {
        public static NoopRelationshipPairMutation Instance { get; } = new();

        private NoopRelationshipPairMutation() { }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed record RelationsSnapshot(
        bool IsFriend,
        bool IsFriendRequestSent,
        bool IsFollowing,
        bool IsBlockedByCurrentUser = false,
        bool IsCurrentUserBlocked = false);

    public interface IFollowingRepository
    {
        Task<IRelationshipPairMutation> AcquireRelationshipPairMutation(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken);
        Task<IUserMutationScope> AcquireUserMutation(
            int userId,
            CancellationToken cancellationToken);
        Task<IUserMutationScope> AcquireUserErasureMutation(
            int userId,
            CancellationToken cancellationToken);
        Task EnsureUsersNotErased(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken);
        Task<UserBlock?> GetBlock(
            int blockerUserId,
            int blockedUserId,
            CancellationToken cancellationToken);
        Task<bool> IsBlockedEitherDirection(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken);
        Task<CategoryFollow?> GetCategoryFollow(int userId, int? categoryId, string? categoryName, CancellationToken cancellationToken);
        Task<Follow?> GetFollow(int userId, int followedId, CancellationToken cancellationToken);
        Task<FriendRequest?> GetFriendRequest(int userId, int friendId, CancellationToken cancellationToken);
        Task<FriendRequest?> GetFriendRequestForApproval(int requestId, int friendId, CancellationToken cancellationToken);
        Task<bool> IsFriend(int userId, int friendId, CancellationToken cancellationToken);
        Task<IReadOnlyList<Friendship>> GetFriendships(int userId, int friendId, CancellationToken cancellationToken);
        Task<FriendshipSubscriptionFeature?> GetSubscriptionFeature(
            string subscriptionId,
            int requesterId,
            int ownerId,
            CancellationToken cancellationToken);
        Task<UserRelationAnalytics> GetOrCreateAnalytics(
            int userId,
            int shardDiscriminator,
            CancellationToken cancellationToken);
        Task Add<T>(T entity, CancellationToken cancellationToken) where T : class;
        Task AddRange<T>(IEnumerable<T> entities, CancellationToken cancellationToken) where T : class;
        void Remove<T>(T entity) where T : class;
        void RemoveRange<T>(IEnumerable<T> entities) where T : class;
        Task SaveChanges(CancellationToken cancellationToken);
    }

    public interface IFollowingReadRepository
    {
        Task<IReadOnlyList<CategoryFollow>> FindCategoryFollows(
            int userId,
            IReadOnlyCollection<int> categoryIds,
            IReadOnlyCollection<string> categoryNames,
            CancellationToken cancellationToken);
        Task<IReadOnlyCollection<int>> FindFollowedUserIds(
            int userId,
            IReadOnlyCollection<int> followedUserIds,
            CancellationToken cancellationToken);
        Task<RelationsSnapshot> GetRelations(int userId, int otherUserId, CancellationToken cancellationToken);
        Task<UserRelationAnalytics> GetUserAnalytics(int userId, CancellationToken cancellationToken);
        Task<FollowingCursorSlice<FriendRequestCursorReadModel>> ListFriendRequestsCursor(
            int currentUserId,
            bool sentRequests,
            FriendRequestStatus status,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
        Task<FollowingCursorSlice<FriendshipCursorReadModel>> ListFriendsCursor(
            int ownerUserId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
        Task<FollowingCursorSlice<FollowCursorReadModel>> ListFollowsCursor(
            int userId,
            bool following,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
        Task<FollowingCursorSlice<CategoryFollowCursorReadModel>> ListCategoryFollowsCursor(
            int userId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
        Task<FollowingCursorSlice<UserBlockCursorReadModel>> ListUserBlocksCursor(
            int blockerUserId,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
        Task<FollowingCursorSlice<RelationIdCursorReadModel>> ListRelationIdsCursor(
            int userId,
            RelationIdKind kind,
            FollowingCursorWindow window,
            CancellationToken cancellationToken);
    }
}
