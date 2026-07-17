using Following.Application.Abstractions;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using StarterEntities;
using System;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddFollowCommand(
        IFollowingRepository Repository,
        IIdentity Identity,
        INotifications Notifications) : Command<FollowDto, Follow>
    {
        public override async Task<Follow> Handle(FollowDto dto)
        {
            var user = Identity.User;
            var existing = await Repository.GetFollow(user.Id, dto.UserId);
            if (existing is not null)
                throw new Exception("Already following.");

            var follow = new Follow(user, new User(dto.UserId, null));
            follow.IsFollowedBack = await Repository.IsFollowing(
                follow.Followed.Id,
                follow.User.Id);

            if (follow.IsFollowedBack)
            {
                var followBack = await Repository.GetFollow(
                    follow.Followed.Id,
                    follow.User.Id);
                followBack.IsFollowedBack = true;
            }

            await Repository.Add(follow);
            await Repository.SaveChanges();
            await Repository.UpdateAnalytics(
                user.Id,
                FollowingAnalyticsField.Followings,
                true);
            await Repository.UpdateAnalytics(
                follow.Followed.Id,
                FollowingAnalyticsField.Followers,
                true);

            Notifications.NewFollower(follow.Followed.Id, follow.User.Id);
            return follow;
        }
    }
}
