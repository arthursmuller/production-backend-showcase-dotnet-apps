using Following.Domain.Exceptions;

namespace Following.Domain.AggregatesModel
{
    /// <summary>
    /// Following owns the authoritative, directional user block. Removing the row means the
    /// block is no longer active; the never-deleted RelationshipEventStream preserves ordering
    /// and the revoked state for downstream privacy projections.
    /// </summary>
    public sealed class UserBlock : FollowingEntity
    {
        public int BlockerUserId { get; private set; }
        public int BlockedUserId { get; private set; }

        private UserBlock() { }

        public static UserBlock Create(
            int blockerUserId,
            int blockedUserId,
            UserBlock? existing)
        {
            if (blockerUserId <= 0 || blockedUserId <= 0)
                throw new FollowingException("Valid block participants are required.");
            if (blockerUserId == blockedUserId)
                throw new FollowingException("A user cannot block themselves.");
            if (existing is not null)
                return existing;

            return new UserBlock
            {
                BlockerUserId = blockerUserId,
                BlockedUserId = blockedUserId,
            };
        }
    }
}
