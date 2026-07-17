using Following.Application.Abstractions;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using StarterEntities;
using System;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddCategoryFollowCommand(
        IFollowingRepository Repository,
        IIdentity Identity,
        ICategories Categories) : Command<CategoryFollowDto, CategoryFollow>
    {
        public override async Task<CategoryFollow> Handle(CategoryFollowDto dto)
        {
            var user = Identity.User;
            var existing = await Repository.GetCategoryFollow(
                user.Id,
                dto.CategoryId,
                dto.CategoryName);

            if (existing is not null)
                throw new Exception("Already following.");

            dto.CategoryId ??= await Categories.GetIdByName(dto.CategoryName);

            var follow = new CategoryFollow(
                user,
                new CategoryRecord(dto.CategoryId.Value, dto.CategoryName));

            await Repository.Add(follow);
            await Repository.SaveChanges();
            await Repository.UpdateAnalytics(
                user.Id,
                FollowingAnalyticsField.CategoryFollowings,
                true);

            Categories.UpdateFollow(
                new[] { dto.CategoryId.Value },
                new[] { dto.CategoryName },
                true);

            return follow;
        }
    }
}
