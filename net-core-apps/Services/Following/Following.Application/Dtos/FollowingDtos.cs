using Following.Domain.AggregatesModel;
using Following.Domain.Dtos;
using Following.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Following.Application.Dtos
{
    public class RequestFriendshipDto
    {
        public int UserId { get; set; }
    }

    public sealed record ApproveFriendRequestInput(
        int RequestId,
        FriendRequestDecisionDomainDto Decision);

    public class FollowDto
    {
        public int UserId { get; set; }
    }

    public class CheckCategoryFollowDto
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
        public string[] Names { get; set; } = Array.Empty<string>();
    }

    public class CheckCategoryFollowResDto
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public bool IsFollowing { get; set; }
    }

    /// <summary>
    /// Stable transport receipt containing only identifiers needed to locate the
    /// relationship status resource.
    /// </summary>
    public sealed record FollowingMutationReceiptDto(
        string RelationshipKind,
        int TargetId,
        int ResourceId);

    public sealed record AddCategoryFollowInput(
        int? CategoryId,
        CategoryFollowDomainDto Details);

    public class RemoveCategoryFollowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class GetRelationsDto
    {
        public bool IsFriend { get; set; }
        public bool IsFriendRequestSent { get; set; }
        public bool IsFollowing { get; set; }
        public bool IsBlockedByCurrentUser { get; set; }
        public bool IsCurrentUserBlocked { get; set; }

        public static GetRelationsDto FromDomain(RelationsSnapshot value) => new()
        {
            IsFriend = value.IsFriend,
            IsFriendRequestSent = value.IsFriendRequestSent,
            IsFollowing = value.IsFollowing,
            IsBlockedByCurrentUser = value.IsBlockedByCurrentUser,
            IsCurrentUserBlocked = value.IsCurrentUserBlocked,
        };
    }

    public sealed class SubscriptionFeatureUpdatedDto : SubscriptionFeatureUpdateDomainDto
    {
        public SubscriptionFeatureUpdatedUserDto Owner { get; }
        public SubscriptionFeatureUpdatedUserDto Requester { get; }
        public string SubscriptionId { get; }

        public SubscriptionFeatureUpdatedDto(
            SubscriptionFeatureUpdatedUserDto owner,
            SubscriptionFeatureUpdatedUserDto requester,
            string subscriptionId,
            DateTime? endDate)
        {
            Owner = owner;
            Requester = requester;
            SubscriptionId = subscriptionId;
            EndDate = endDate;
        }
    }

    public sealed record SubscriptionFeatureUpdatedUserDto(int Id, string Name);

    public static class DtoConversor
    {
        public static IEnumerable<CheckCategoryFollowResDto> Convert(
            CheckCategoryFollowDto dto,
            List<CategoryFollow> follows)
        {
            List<CheckCategoryFollowResDto> byName = new(), byId = new();

            foreach (var id in dto.Ids)
            {
                var follow = follows?.FirstOrDefault(item => item.CategoryId == id);
                byId.Add(new CheckCategoryFollowResDto
                {
                    Id = id,
                    IsFollowing = follow is not null,
                    Name = follow?.CategoryName
                });
            }

            foreach (var name in dto.Names)
            {
                var follow = follows?.FirstOrDefault(item => item.CategoryName == name);
                byName.Add(new CheckCategoryFollowResDto
                {
                    Id = follow?.CategoryId,
                    IsFollowing = follow is not null,
                    Name = name
                });
            }

            return byName.Concat(byId);
        }
    }
}
