using Following.Application.Abstractions;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFriendCommand(
        IFollowingRepository Repository,
        IIdentity Identity) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id)
        {
            var friendships = await Repository.GetFriendships(Identity.User.Id, id);

            Repository.RemoveRange(friendships);
            await Repository.SaveChanges();
            await Repository.UpdateAnalytics(
                friendships[0].User.Id,
                FollowingAnalyticsField.Friends,
                false);
            await Repository.UpdateAnalytics(
                friendships[1].User.Id,
                FollowingAnalyticsField.Friends,
                false);

            return true;
        }
    }
}
