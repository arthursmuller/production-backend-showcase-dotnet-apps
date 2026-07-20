using Following.Domain.Dtos;
using Following.Domain.Exceptions;
using StarterEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Following.Domain.AggregatesModel
{
    public static class FollowingFieldLimits
    {
        public const int DisplayName = 200;
        public const int CategoryName = 128;
        public const int SubscriptionId = 191;
    }

    public enum FriendRequestStatus : byte
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,
        Cancelled = 3
    }

    public abstract class FollowingEntity : BaseEntity
    {
        public long Version { get; private set; } = 1;

        protected FollowingEntity() { }

        protected FollowingEntity(int id)
            : base(id)
        {
        }
    }

    public class FriendRequest : FollowingEntity
    {
        public int SenderUserId { get; private set; }
        public string? SenderDisplayName { get; private set; }
        public int RecipientUserId { get; private set; }
        public string? RecipientDisplayName { get; private set; }
        public FriendRequestStatus Status { get; private set; }

        public static FriendRequest Create(
            User user,
            User friend,
            FriendRequest? existingRequest,
            bool alreadyFriends)
        {
            FollowingGuards.ValidUsers(user, friend);

            if (alreadyFriends)
                throw new FollowingException("The users are already friends.");
            if (existingRequest is not null)
                throw new FollowingException("A friend request already exists between these users.");

            return new FriendRequest
            {
                SenderUserId = user.Id,
                SenderDisplayName = FollowingGuards.OptionalDisplayName(user.FullName),
                RecipientUserId = friend.Id,
                RecipientDisplayName = FollowingGuards.OptionalDisplayName(friend.FullName),
                Status = FriendRequestStatus.Pending
            };
        }

        public bool Decide(User actor, FriendRequestDecisionDomainDto dto)
        {
            if (dto is null)
                throw new FollowingException("A friend request decision is required.");
            if (actor?.Id != RecipientUserId)
                throw new FollowingException("Only the requested user can decide this friend request.");
            if (Status != FriendRequestStatus.Pending)
                throw new FollowingException("This friend request has already been decided.");

            Status = dto.Approve
                ? FriendRequestStatus.Approved
                : FriendRequestStatus.Rejected;
            return dto.Approve;
        }

        public void Cancel(User actor)
        {
            if (actor is null || (actor.Id != SenderUserId && actor.Id != RecipientUserId))
                throw new FollowingException("Only a participant can cancel this friend request.");
            if (Status != FriendRequestStatus.Pending)
                throw new FollowingException("Only a pending friend request can be cancelled.");

            Status = FriendRequestStatus.Cancelled;
        }

        private FriendRequest() { }
    }

    public class Friendship : FollowingEntity
    {
        public int UserId { get; private set; }
        public string? UserDisplayName { get; private set; }
        public int FriendUserId { get; private set; }
        public string? FriendDisplayName { get; private set; }

        public static IReadOnlyList<Friendship> CreatePair(
            User user,
            User friend,
            bool alreadyFriends)
        {
            FollowingGuards.ValidUsers(user, friend);
            if (alreadyFriends)
                throw new FollowingException("The users are already friends.");

            return CreatePair(user, friend);
        }

        public static IReadOnlyList<Friendship> CreatePairIfMissing(
            User user,
            User friend,
            bool alreadyFriends)
        {
            if (alreadyFriends)
                return Array.Empty<Friendship>();

            FollowingGuards.ValidUsers(user, friend);
            return CreatePair(user, friend);
        }

        private static IReadOnlyList<Friendship> CreatePair(User user, User friend) =>
            new[]
            {
                new Friendship(user, friend),
                new Friendship(friend, user)
            };

        public static IReadOnlyList<Friendship> ValidatePairForRemoval(
            IReadOnlyList<Friendship> friendships,
            int userId,
            int friendId)
        {
            if (friendships is null || friendships.Count != 2)
                throw new FollowingException("The friendship pair is incomplete or does not exist.");

            var expectedDirections = friendships.Any(item =>
                item.UserId == userId && item.FriendUserId == friendId) &&
                friendships.Any(item =>
                    item.UserId == friendId && item.FriendUserId == userId);

            if (!expectedDirections)
                throw new FollowingException("The friendship pair does not match the requested users.");

            return friendships;
        }

        private Friendship(User user, User friend)
        {
            UserId = user.Id;
            UserDisplayName = FollowingGuards.OptionalDisplayName(user.FullName);
            FriendUserId = friend.Id;
            FriendDisplayName = FollowingGuards.OptionalDisplayName(friend.FullName);
        }

        private Friendship() { }
    }

    public class Follow : FollowingEntity
    {
        public int FollowerUserId { get; private set; }
        public string? FollowerDisplayName { get; private set; }
        public int FollowedUserId { get; private set; }
        public string? FollowedDisplayName { get; private set; }
        public bool IsFollowedBack { get; private set; }

        public static Follow Create(User user, User followed, Follow? existing)
        {
            FollowingGuards.ValidUsers(user, followed);
            if (existing is not null)
                throw new FollowingException("The user is already being followed.");

            return new Follow(user, followed);
        }

        public void LinkReciprocal(Follow? reciprocal)
        {
            if (reciprocal is null)
                return;
            if (reciprocal.FollowerUserId != FollowedUserId ||
                reciprocal.FollowedUserId != FollowerUserId)
            {
                throw new FollowingException("The reciprocal follow does not match these users.");
            }

            IsFollowedBack = true;
            reciprocal.IsFollowedBack = true;
        }

        public void UnlinkReciprocal(Follow? reciprocal)
        {
            IsFollowedBack = false;
            if (reciprocal is null)
                return;
            if (reciprocal.FollowerUserId != FollowedUserId ||
                reciprocal.FollowedUserId != FollowerUserId)
            {
                throw new FollowingException("The reciprocal follow does not match these users.");
            }

            reciprocal.IsFollowedBack = false;
        }

        private Follow(User user, User followed)
        {
            FollowerUserId = user.Id;
            FollowerDisplayName = FollowingGuards.OptionalDisplayName(user.FullName);
            FollowedUserId = followed.Id;
            FollowedDisplayName = FollowingGuards.OptionalDisplayName(followed.FullName);
            IsFollowedBack = false;
        }

        private Follow() { }
    }

    public class CategoryFollow : FollowingEntity
    {
        public int UserId { get; private set; }
        public string? UserDisplayName { get; private set; }
        public int CategoryId { get; private set; }
        public string? CategoryName { get; private set; }

        public static CategoryFollow Create(
            User user,
            int categoryId,
            CategoryFollowDomainDto dto,
            CategoryFollow? existing)
        {
            if (dto is null)
                throw new FollowingException("Category follow data is required.");
            if (user is null || user.Id <= 0)
                throw new FollowingException("A valid user is required.");
            if (categoryId <= 0)
                throw new FollowingException("A valid category is required.");
            if (existing is not null)
                throw new FollowingException("The category is already being followed.");

            return new CategoryFollow(
                user,
                categoryId,
                FollowingGuards.RequiredBoundedValue(
                    dto.CategoryName,
                    FollowingFieldLimits.CategoryName,
                    "category name"));
        }

        private CategoryFollow(User user, int categoryId, string categoryName)
        {
            UserId = user.Id;
            UserDisplayName = FollowingGuards.OptionalDisplayName(user.FullName);
            CategoryId = categoryId;
            CategoryName = categoryName;
        }

        private CategoryFollow() { }
    }

    public class UserRelationAnalytics : FollowingEntity
    {
        public const int ShardCount = 64;

        public byte ShardId { get; private set; }
        public int TotalFriends { get; private set; }
        public int TotalFollowers { get; private set; }
        public int TotalFollowing { get; private set; }
        public int TotalCategoriesFollowed { get; private set; }
        public int TotalFriendsRequests { get; private set; }

        public UserRelationAnalytics(int id)
            : this(id, shardDiscriminator: 0)
        {
        }

        public UserRelationAnalytics(int id, int shardDiscriminator)
            : base(id)
        {
            if (id <= 0)
                throw new FollowingException("A valid analytics user identifier is required.");

            ShardId = ResolveShard(shardDiscriminator);
        }

        private UserRelationAnalytics() { }

        public void IncrementFriends() => TotalFriends++;
        public void DecrementFriends() => TotalFriends = NonNegative(TotalFriends - 1);
        public void IncrementFollowers() => TotalFollowers++;
        public void DecrementFollowers() => TotalFollowers = NonNegative(TotalFollowers - 1);
        public void IncrementFollowing() => TotalFollowing++;
        public void DecrementFollowing() => TotalFollowing = NonNegative(TotalFollowing - 1);
        public void IncrementCategoryFollowings() => TotalCategoriesFollowed++;
        public void DecrementCategoryFollowings() => TotalCategoriesFollowed = NonNegative(TotalCategoriesFollowed - 1);
        public void IncrementFriendRequests() => TotalFriendsRequests++;
        public void DecrementFriendRequests() => TotalFriendsRequests = NonNegative(TotalFriendsRequests - 1);

        public static byte ResolveShard(int discriminator)
        {
            // Multiplicative hashing keeps stable counterpart/category keys evenly distributed
            // across a fixed power-of-two shard count without introducing runtime randomness.
            var mixed = unchecked((uint)discriminator * 2654435761U);
            return (byte)(mixed & (ShardCount - 1));
        }

        public static UserRelationAnalytics Aggregate(
            int userId,
            int totalFriends,
            int totalFollowers,
            int totalFollowing,
            int totalCategoriesFollowed,
            int totalFriendsRequests) =>
            new(userId)
            {
                TotalFriends = NonNegative(totalFriends),
                TotalFollowers = NonNegative(totalFollowers),
                TotalFollowing = NonNegative(totalFollowing),
                TotalCategoriesFollowed = NonNegative(totalCategoriesFollowed),
                TotalFriendsRequests = NonNegative(totalFriendsRequests),
            };

        private static int NonNegative(int value) => Math.Max(0, value);
    }

    public class FriendshipSubscriptionFeature : FollowingEntity
    {
        public enum TransitionKind
        {
            NoChange,
            Add,
            Update,
            Remove
        }

        public sealed record Transition(
            TransitionKind Kind,
            FriendshipSubscriptionFeature? Feature);

        public int OwnerId { get; private set; }
        public int RequesterId { get; private set; }
        public string SubscriptionId { get; private set; } = string.Empty;
        public DateTime EndDate { get; private set; }

        public static Transition Apply(
            FriendshipSubscriptionFeature? existing,
            int ownerId,
            int requesterId,
            string subscriptionId,
            SubscriptionFeatureUpdateDomainDto dto,
            DateTime utcNow)
        {
            if (dto?.EndDate is null)
            {
                if (existing is null)
                    return new Transition(TransitionKind.NoChange, null);

                existing.ValidateCancellation(ownerId, requesterId, subscriptionId);
                return new Transition(TransitionKind.Remove, existing);
            }

            if (existing is null)
            {
                var created = Create(
                    ownerId,
                    requesterId,
                    subscriptionId,
                    dto,
                    utcNow);
                return new Transition(TransitionKind.Add, created);
            }

            existing.Renew(dto, utcNow);
            return new Transition(TransitionKind.Update, existing);
        }

        public static FriendshipSubscriptionFeature Create(
            int ownerId,
            int requesterId,
            string subscriptionId,
            SubscriptionFeatureUpdateDomainDto dto,
            DateTime utcNow)
        {
            if (dto?.EndDate is null)
                throw new FollowingException("A subscription end date is required.");

            var normalizedSubscriptionId = FollowingGuards.RequiredBoundedValue(
                subscriptionId,
                FollowingFieldLimits.SubscriptionId,
                "subscription identifier");
            Validate(ownerId, requesterId, dto.EndDate.Value, utcNow);
            return new FriendshipSubscriptionFeature(
                ownerId,
                requesterId,
                normalizedSubscriptionId,
                dto.EndDate.Value);
        }

        public void Renew(SubscriptionFeatureUpdateDomainDto dto, DateTime utcNow)
        {
            if (dto?.EndDate is null)
                throw new FollowingException("A subscription end date is required.");

            Validate(OwnerId, RequesterId, dto.EndDate.Value, utcNow);
            EndDate = dto.EndDate.Value;
        }

        public void ValidateCancellation(int ownerId, int requesterId, string subscriptionId)
        {
            if (OwnerId != ownerId ||
                RequesterId != requesterId ||
                !string.Equals(SubscriptionId, subscriptionId?.Trim(), StringComparison.Ordinal))
            {
                throw new FollowingException("The subscription cancellation does not match the stored feature.");
            }
        }

        private static void Validate(
            int ownerId,
            int requesterId,
            DateTime endDate,
            DateTime utcNow)
        {
            if (ownerId <= 0 || requesterId <= 0)
                throw new FollowingException("Valid subscription participants are required.");
            if (ownerId == requesterId)
                throw new FollowingException("A user cannot subscribe to their own networking feature.");
            if (endDate <= utcNow)
                throw new FollowingException("The subscription end date must be in the future.");
        }

        private FriendshipSubscriptionFeature(
            int ownerId,
            int requesterId,
            string subscriptionId,
            DateTime endDate)
        {
            OwnerId = ownerId;
            RequesterId = requesterId;
            SubscriptionId = subscriptionId;
            EndDate = endDate;
        }

        private FriendshipSubscriptionFeature() { }
    }

    internal static class FollowingGuards
    {
        internal static void ValidUsers(User user, User other)
        {
            if (user is null || user.Id <= 0 || other is null || other.Id <= 0)
                throw new FollowingException("Valid users are required.");
            if (user.Id == other.Id)
                throw new FollowingException("A user cannot create a relationship with themselves.");

            OptionalDisplayName(user.FullName);
            OptionalDisplayName(other.FullName);
        }

        internal static string? OptionalDisplayName(string? value)
        {
            if (value is null)
                return null;

            var normalized = value.Trim();
            if (normalized.Length == 0)
                return null;
            if (normalized.Length > FollowingFieldLimits.DisplayName)
            {
                throw new FollowingException(
                    $"Display names cannot exceed {FollowingFieldLimits.DisplayName} characters.");
            }

            return normalized;
        }

        internal static string RequiredBoundedValue(string? value, int maxLength, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FollowingException($"A {fieldName} is required.");

            var normalized = value.Trim();
            if (normalized.Length > maxLength)
                throw new FollowingException($"The {fieldName} cannot exceed {maxLength} characters.");

            return normalized;
        }
    }
}
