using Following.Application.Abstractions;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFollowingCommand(
        IFollowingRepository Repository,
        IIdentity Identity) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id)
        {
            var user = Identity.User;
            var existing = await Repository.GetFollow(user.Id, id);
            var followBack = await Repository.GetFollow(existing.Followed.Id, user.Id);

            if (followBack is not null)
                followBack.IsFollowedBack = false;

            Repository.Remove(existing);
            await Repository.SaveChanges();
            await Repository.UpdateAnalytics(
                user.Id,
                FollowingAnalyticsField.Followings,
                false);
            await Repository.UpdateAnalytics(
                existing.Followed.Id,
                FollowingAnalyticsField.Followers,
                false);

            return true;
        }
    }
}
