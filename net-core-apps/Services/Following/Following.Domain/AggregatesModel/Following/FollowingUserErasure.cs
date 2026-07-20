using System;

namespace Following.Domain.AggregatesModel
{
    public enum FollowingUserErasureStatus : byte
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
    }

    public enum FollowingUserErasureStage : byte
    {
        FollowsAsFollower = 0,
        FollowsAsFollowed = 1,
        FriendshipsAsOwner = 2,
        FriendshipsAsFriend = 3,
        FriendRequestsAsSender = 4,
        FriendRequestsAsRecipient = 5,
        BlocksAsBlocker = 6,
        BlocksAsBlocked = 7,
        SubscriptionsAsOwner = 8,
        SubscriptionsAsRequester = 9,
        CategoryFollows = 10,
        RelationshipStreamsAsOwner = 11,
        RelationshipStreamsAsViewer = 12,
        Analytics = 13,
        Complete = 14,
    }

    /// <summary>
    /// Minimal, durable privacy authority. The user/version coordinate is retained after
    /// completion so delayed commands and projection rebuilds can never recreate erased data.
    /// </summary>
    public sealed class FollowingUserErasure
    {
        public int UserId { get; private set; }
        public Guid RequestId { get; private set; }
        public long ErasureVersion { get; private set; }
        public DateTime RequestedAtUtc { get; private set; }
        public FollowingUserErasureStatus Status { get; private set; }
        public FollowingUserErasureStage Stage { get; private set; }
        public long Cursor { get; private set; }
        public string? LeaseOwner { get; private set; }
        public long LeaseToken { get; private set; }
        public DateTime? LeaseExpiresAtUtc { get; private set; }
        public int AttemptCount { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime UpdatedAtUtc { get; private set; }
        public DateTime? CompletedAtUtc { get; private set; }
        public Guid? CompletionEventId { get; private set; }
        public long RowVersion { get; private set; }

        private FollowingUserErasure() { }

        public static FollowingUserErasure Create(
            int userId,
            Guid requestId,
            long erasureVersion,
            DateTime requestedAtUtc,
            DateTime nowUtc)
        {
            ValidateRequest(userId, requestId, erasureVersion);
            return new FollowingUserErasure
            {
                UserId = userId,
                RequestId = requestId,
                ErasureVersion = erasureVersion,
                RequestedAtUtc = NormalizeUtc(requestedAtUtc),
                Status = FollowingUserErasureStatus.Pending,
                Stage = FollowingUserErasureStage.FollowsAsFollower,
                Cursor = 0,
                CreatedAtUtc = NormalizeUtc(nowUtc),
                UpdatedAtUtc = NormalizeUtc(nowUtc),
                RowVersion = 1,
            };
        }

        public void ScheduleNewer(
            Guid requestId,
            long erasureVersion,
            DateTime requestedAtUtc,
            DateTime nowUtc)
        {
            ValidateRequest(UserId, requestId, erasureVersion);
            if (erasureVersion <= ErasureVersion)
                throw new InvalidOperationException("Only a newer erasure version can replace this tombstone.");

            RequestId = requestId;
            ErasureVersion = erasureVersion;
            RequestedAtUtc = NormalizeUtc(requestedAtUtc);
            Status = FollowingUserErasureStatus.Pending;
            Stage = FollowingUserErasureStage.FollowsAsFollower;
            Cursor = 0;
            LeaseOwner = null;
            LeaseExpiresAtUtc = null;
            CompletedAtUtc = null;
            CompletionEventId = null;
            UpdatedAtUtc = NormalizeUtc(nowUtc);
        }

        public void Claim(string owner, DateTime leaseExpiresAtUtc, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128)
                throw new ArgumentException("A bounded lease owner is required.", nameof(owner));
            if (Status == FollowingUserErasureStatus.Completed)
                throw new InvalidOperationException("A completed erasure cannot be claimed.");

            var now = NormalizeUtc(nowUtc);
            var expiry = NormalizeUtc(leaseExpiresAtUtc);
            if (expiry <= now)
                throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc));

            LeaseOwner = owner;
            LeaseToken = checked(LeaseToken + 1);
            LeaseExpiresAtUtc = expiry;
            Status = FollowingUserErasureStatus.Processing;
            AttemptCount = checked(AttemptCount + 1);
            UpdatedAtUtc = now;
        }

        public bool OwnsLease(string owner, long token, DateTime nowUtc) =>
            Status == FollowingUserErasureStatus.Processing
            && string.Equals(LeaseOwner, owner, StringComparison.Ordinal)
            && LeaseToken == token
            && LeaseExpiresAtUtc > NormalizeUtc(nowUtc);

        public void Advance(
            FollowingUserErasureStage stage,
            long cursor,
            string owner,
            long token,
            DateTime leaseExpiresAtUtc,
            DateTime nowUtc)
        {
            if (!OwnsLease(owner, token, nowUtc))
                throw new InvalidOperationException("The erasure lease was lost.");
            if (stage < Stage || cursor < 0)
                throw new InvalidOperationException("Erasure progress cannot move backwards.");

            Stage = stage;
            Cursor = cursor;
            LeaseExpiresAtUtc = NormalizeUtc(leaseExpiresAtUtc);
            UpdatedAtUtc = NormalizeUtc(nowUtc);
        }

        public void Complete(Guid eventId, string owner, long token, DateTime nowUtc)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("A completion event ID is required.", nameof(eventId));
            if (!OwnsLease(owner, token, nowUtc) || Stage != FollowingUserErasureStage.Complete)
                throw new InvalidOperationException("Only the fenced final stage can complete erasure.");

            var now = NormalizeUtc(nowUtc);
            Status = FollowingUserErasureStatus.Completed;
            Cursor = 0;
            CompletionEventId = eventId;
            CompletedAtUtc = now;
            LeaseOwner = null;
            LeaseExpiresAtUtc = null;
            UpdatedAtUtc = now;
        }

        public void Release(string owner, long token, DateTime nowUtc)
        {
            if (!OwnsLease(owner, token, nowUtc))
                return;

            LeaseOwner = null;
            LeaseExpiresAtUtc = null;
            Status = FollowingUserErasureStatus.Pending;
            UpdatedAtUtc = NormalizeUtc(nowUtc);
        }

        private static void ValidateRequest(int userId, Guid requestId, long erasureVersion)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            if (requestId == Guid.Empty)
                throw new ArgumentException("A request ID is required.", nameof(requestId));
            if (erasureVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(erasureVersion));
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
                throw new ArgumentException("A timestamp is required.", nameof(value));
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }
    }
}
