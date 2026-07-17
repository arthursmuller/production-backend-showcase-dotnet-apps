using Following.Application.Abstractions;
using StarterEntities;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record ApproveFriendRequestCommand(
        IFollowingRepository Repository,
        IIdentity Identity,
        INotifications Notifications) : Command<ApproveDto, bool>
    {
        public override async Task<bool> Handle(ApproveDto dto)
        {
            var user = Identity.User;
            var existing = await Repository.GetFriendRequestForApproval(dto.Id, user.Id);

            if (!dto.Approve)
                return false;

            await Repository.AddFriendship(existing.User, user);
            Repository.Remove(existing);
            await Repository.SaveChanges();
            Notifications.FriendRequestAccepted(existing.User.Id, existing.Friend.Id);
            return true;
        }
    }
}
