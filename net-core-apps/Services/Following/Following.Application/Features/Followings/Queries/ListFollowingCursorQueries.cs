using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using IdentityProvider;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public sealed record ListFriendRequestsCursorQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity,
        FollowingCursorCodec CursorCodec)
        : Query<FriendRequestsCursorCriteria, FollowingCursorPage<FriendRequestCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<FriendRequestCursorItemDto>> Handle(
            FriendRequestsCursorCriteria criteria,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            var actorId = CursorQuerySupport.RequireActor(Identity);
            if (!Enum.IsDefined(criteria.Status))
                throw new FollowingException("Status is not a supported friend-request status.");
            var fingerprint = FollowingCursorFingerprint.Create(
                "friend_requests",
                CursorQuerySupport.UserActor(actorId),
                criteria.SentRequests ? "sent" : "received",
                ((byte)criteria.Status).ToString(CultureInfo.InvariantCulture));
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListFriendRequestsCursor(
                actorId,
                criteria.SentRequests,
                criteria.Status,
                window,
                cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new FriendRequestCursorItemDto(
                    item.RowId,
                    item.SenderUserId,
                    item.SenderDisplayName,
                    item.RecipientUserId,
                    item.RecipientDisplayName,
                    item.Status,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }
    }

    public sealed record ListFriendsCursorQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity,
        FollowingCursorCodec CursorCodec)
        : Query<FriendsCursorCriteria, FollowingCursorPage<FriendshipCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<FriendshipCursorItemDto>> Handle(
            FriendsCursorCriteria criteria,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            var actorId = CursorQuerySupport.RequireActor(Identity);
            var ownerId = criteria.UserId ?? actorId;
            FollowingInputLimits.RequirePositiveId(ownerId, nameof(criteria.UserId));
            var fingerprint = FollowingCursorFingerprint.Create(
                "friends",
                CursorQuerySupport.UserActor(actorId),
                ownerId.ToString(CultureInfo.InvariantCulture));
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListFriendsCursor(ownerId, window, cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new FriendshipCursorItemDto(
                    item.RowId,
                    item.UserId,
                    item.FriendUserId,
                    item.FriendDisplayName,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }
    }

    public sealed record ListFollowsCursorQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity,
        FollowingCursorCodec CursorCodec)
        : Query<FollowsCursorCriteria, FollowingCursorPage<FollowCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<FollowCursorItemDto>> Handle(
            FollowsCursorCriteria criteria,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            var actorId = CursorQuerySupport.RequireActor(Identity);
            FollowingInputLimits.RequirePositiveId(criteria.UserId, nameof(criteria.UserId));
            var fingerprint = FollowingCursorFingerprint.Create(
                "user_follows",
                CursorQuerySupport.UserActor(actorId),
                criteria.UserId.ToString(CultureInfo.InvariantCulture),
                criteria.Following ? "following" : "followers");
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListFollowsCursor(
                criteria.UserId,
                criteria.Following,
                window,
                cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new FollowCursorItemDto(
                    item.RowId,
                    item.FollowerUserId,
                    item.FollowerDisplayName,
                    item.FollowedUserId,
                    item.FollowedDisplayName,
                    item.IsFollowedBack,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }
    }

    public sealed record ListCategoryFollowsCursorQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity,
        FollowingCursorCodec CursorCodec)
        : Query<CategoryFollowsCursorCriteria, FollowingCursorPage<CategoryFollowCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<CategoryFollowCursorItemDto>> Handle(
            CategoryFollowsCursorCriteria criteria,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            var actorId = CursorQuerySupport.RequireActor(Identity);
            FollowingInputLimits.RequirePositiveId(criteria.UserId, nameof(criteria.UserId));
            var fingerprint = FollowingCursorFingerprint.Create(
                "category_follows",
                CursorQuerySupport.UserActor(actorId),
                criteria.UserId.ToString(CultureInfo.InvariantCulture));
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListCategoryFollowsCursor(
                criteria.UserId,
                window,
                cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new CategoryFollowCursorItemDto(
                    item.RowId,
                    item.UserId,
                    item.CategoryId,
                    item.CategoryName,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }
    }

    public sealed record ListUserBlocksCursorQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity,
        FollowingCursorCodec CursorCodec)
        : Query<UserBlocksCursorCriteria, FollowingCursorPage<UserBlockCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<UserBlockCursorItemDto>> Handle(
            UserBlocksCursorCriteria criteria,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            var actorId = CursorQuerySupport.RequireActor(Identity);
            var fingerprint = FollowingCursorFingerprint.Create(
                "user_blocks",
                CursorQuerySupport.UserActor(actorId));
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListUserBlocksCursor(
                actorId,
                window,
                cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new UserBlockCursorItemDto(
                    item.BlockedUserId,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }
    }

    public sealed record ListRelationIdsCursorQuery(
        IFollowingReadRepository Repository,
        FollowingCursorCodec CursorCodec)
        : Query<RelationIdsCursorInput, FollowingCursorPage<RelationIdCursorItemDto>>
    {
        public override async Task<FollowingCursorPage<RelationIdCursorItemDto>> Handle(
            RelationIdsCursorInput input,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            FollowingInputLimits.RequirePositiveId(input.UserId, nameof(input.UserId));
            if (!Enum.IsDefined(input.Kind))
                throw new FollowingException("Kind must be friends, followers, or categories.");
            if (string.IsNullOrWhiteSpace(input.ActorScope) || input.ActorScope.Length > 512)
                throw new FollowingException("An authenticated bounded client scope is required.", 403);

            var fingerprint = FollowingCursorFingerprint.Create(
                "administrative_relation_ids",
                input.ActorScope,
                input.UserId.ToString(CultureInfo.InvariantCulture),
                input.Kind.ToString().ToLowerInvariant());
            var criteria = new AdministrativeCursorCriteria
            {
                PageSize = input.PageSize,
                Cursor = input.Cursor,
            };
            var window = CursorQuerySupport.CreateWindow(criteria, fingerprint, CursorCodec);
            var slice = await Repository.ListRelationIdsCursor(
                input.UserId,
                input.Kind,
                window,
                cancellationToken);
            return CursorQuerySupport.ToPage(
                slice,
                fingerprint,
                CursorCodec,
                item => new RelationIdCursorItemDto(
                    item.RelationId,
                    CursorQuerySupport.NormalizeUtc(item.CreatedDateUtc)));
        }

        private sealed class AdministrativeCursorCriteria : FollowingCursorCriteria;
    }

    internal static class CursorQuerySupport
    {
        internal static int RequireActor(IIdentityService identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            var actorId = identity.User?.Id ?? 0;
            if (actorId <= 0)
                throw new FollowingException("An authenticated user identity is required.", 401);
            return actorId;
        }

        internal static string UserActor(int actorId) =>
            "user:" + actorId.ToString(CultureInfo.InvariantCulture);

        internal static FollowingCursorWindow CreateWindow(
            FollowingCursorCriteria criteria,
            byte[] fingerprint,
            FollowingCursorCodec codec)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            ArgumentNullException.ThrowIfNull(codec);
            if (criteria.PageSize is < 1 or > FollowingReadLimits.MaximumCursorPageSize)
            {
                throw new FollowingException(
                    $"PageSize must be between 1 and {FollowingReadLimits.MaximumCursorPageSize}.");
            }

            FollowingCursorPosition? after = null;
            if (criteria.Cursor is not null)
            {
                if (criteria.Cursor.Length == 0 ||
                    criteria.Cursor.Length > FollowingCursorCodec.MaximumEncodedLength ||
                    !codec.TryDecode(criteria.Cursor, fingerprint, out after))
                {
                    throw new FollowingException("The cursor is invalid, expired, or belongs to another scope.");
                }
            }

            return new FollowingCursorWindow(criteria.PageSize, after);
        }

        internal static FollowingCursorPage<TDto> ToPage<TRead, TDto>(
            FollowingCursorSlice<TRead> slice,
            byte[] fingerprint,
            FollowingCursorCodec codec,
            Func<TRead, TDto> map)
            where TRead : IFollowingCursorReadModel
        {
            ArgumentNullException.ThrowIfNull(slice);
            ArgumentNullException.ThrowIfNull(map);
            var items = slice.Items.Select(map).ToArray();
            string? nextCursor = null;
            if (slice.HasMore)
            {
                var nextPosition = slice.NextPosition
                    ?? throw new InvalidOperationException(
                        "A continuing cursor page requires a next position.");
                nextCursor = codec.Encode(nextPosition, fingerprint);
            }

            return new FollowingCursorPage<TDto>(items, nextCursor, slice.HasMore);
        }

        internal static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
