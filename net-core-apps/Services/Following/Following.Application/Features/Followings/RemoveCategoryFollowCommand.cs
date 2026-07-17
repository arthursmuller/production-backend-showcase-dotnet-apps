using Following.Application.Abstractions;
using Following.Application.Dtos;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveCategoryFollowCommand(
        IFollowingRepository Repository,
        IIdentity Identity,
        ICategories Categories) : Command<RemoveCategoryFollowDto, bool>
    {
        public override async Task<bool> Handle(RemoveCategoryFollowDto dto)
        {
            var user = Identity.User;
            var follow = await Repository.GetCategoryFollow(user.Id, dto.Id, dto.Name);

            Repository.Remove(follow);
            await Repository.SaveChanges();
            await Repository.UpdateAnalytics(
                user.Id,
                FollowingAnalyticsField.CategoryFollowings,
                false);

            Categories.UpdateFollow(
                new[] { dto.Id },
                new[] { dto.Name },
                false);

            return true;
        }
    }
}
