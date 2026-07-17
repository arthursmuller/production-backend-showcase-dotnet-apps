using Following.Domain.AggregatesModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Following.Application.Dtos
{
    public class FriendsRequestCriteria : CommomFilter
    {
        public bool SentRequests { get; set; }
        public bool? Pending { get; set; }
    }

    public class FriendsCriteria : CommomFilter
    {
        public int? UserId { get; set; }
    }

    public class FollowsCriteria : CommomFilter
    {
        public int UserId { get; set; }
        public bool Following { get; set; }
    }

    public class CategoryFollowsCriteria : CommomFilter
    {
        public int UserId { get; set; }
    }

    public class RequestFriendshipDto
    {
        public int UserId { get; set; }
    }

    public class FollowDto
    {
        public int UserId { get; set; }
    }

    public class CheckCategoryFollowDto
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
        public string[] Names { get; set; } = Array.Empty<string>();
    }

    public class CheckUserFollowDto
    {
        public int[] Ids { get; set; }
    }

    public class CheckCategoryFollowResDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public bool IsFollowing { get; set; }
    }

    public class CheckUserFollowResDto
    {
        public int Id { get; set; }
        public bool IsFollowing { get; set; }
    }

    public class CategoryFollowDto
    {
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; }
    }

    public class RemoveCategoryFollowDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class GetRelationsDto
    {
        public bool IsFriend { get; set; }
        public bool IsFriendRequestSent { get; set; }
        public bool IsFollowing { get; set; }
    }

    public class GetPendingRequestsDto
    {
        public bool HasFriendRequestPending { get; set; }
    }

    public class GetUserFollowersAndFriendsIdDto
    {
        public int[] Friends { get; set; }
        public int[] Followers { get; set; }
        public int[] Categories { get; set; }
    }

    public class SubscriptionFeatureUpdatedDto
    {
        public SubscriptionFeatureUpdatedUserDto Owner;
        public SubscriptionFeatureUpdatedUserDto Requester;
        public string SubscriptionId;
        public DateTime? EndDate;

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

        public SubscriptionFeatureUpdatedDto() { }
    }

    public class SubscriptionFeatureUpdatedUserDto
    {
        public int Id;
        public string Name;

        public SubscriptionFeatureUpdatedUserDto(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public SubscriptionFeatureUpdatedUserDto() { }

        public static implicit operator int(SubscriptionFeatureUpdatedUserDto value) => value.Id;
    }

    public static class DtoConversor
    {
        public static IEnumerable<CheckCategoryFollowResDto> Convert(
            CheckCategoryFollowDto dto,
            List<CategoryFollow> follows)
        {
            List<CheckCategoryFollowResDto> byName = new(), byId = new();

            foreach (var id in dto.Ids)
            {
                var follow = follows?.FirstOrDefault(item => item.Followed.Id == id);
                byId.Add(new CheckCategoryFollowResDto
                {
                    Id = id,
                    IsFollowing = follow is not null,
                    Name = follow?.Followed?.Name
                });
            }

            foreach (var name in dto.Names)
            {
                var follow = follows?.FirstOrDefault(item => item.Followed.Name == name);
                byName.Add(new CheckCategoryFollowResDto
                {
                    Id = follow?.Followed?.Id,
                    IsFollowing = follow is not null,
                    Name = name
                });
            }

            return byName.Concat(byId);
        }
    }
}
