using Following.Application.Abstractions;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentity Identity) : Command<int, bool>
    {
        public override async Task<bool> Handle(int id)
        {
            var existing = await Repository.GetFriendRequest(Identity.User.Id, id);
            Repository.Remove(existing);
            await Repository.SaveChanges();
            return true;
        }
    }
}
