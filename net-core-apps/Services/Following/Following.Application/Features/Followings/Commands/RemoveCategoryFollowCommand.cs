using Following.Domain.Repositories;
using Following.Application.Dtos;
using Following.Application.Providers.Categories;
using Following.Domain.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record RemoveCategoryFollowCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        CategoriesProvider Categories) : Command<RemoveCategoryFollowDto, bool>
    {
        public override async Task<bool> Handle(
            RemoveCategoryFollowDto dto,
            CancellationToken cancellationToken)
        {
            if (dto.Id <= 0)
                FollowingInputLimits.ValidateCategoryName(dto.Name);

            var identity = Identity.User;
            if (identity?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var user = new StarterEntities.User(identity.Id, identity.Name);
            await using var userMutation = await Repository.AcquireUserMutation(
                user.Id,
                cancellationToken);
            await Repository.EnsureUsersNotErased([user.Id], cancellationToken);
            var follow = await Repository.GetCategoryFollow(
                user.Id,
                dto.Id > 0 ? dto.Id : null,
                dto.Name,
                cancellationToken);
            if (follow is null)
                throw new FollowingException("The category follow was not found.", 404);

            var analytics = await Repository.GetOrCreateAnalytics(
                user.Id, follow.CategoryId, cancellationToken);
            analytics.DecrementCategoryFollowings();

            Repository.Remove(follow);
            var categoryNames = follow.CategoryName is { Length: > 0 } categoryName
                ? new[] { categoryName }
                : Array.Empty<string>();
            await Categories.UpdateFollowAsync(
                new[] { follow.CategoryId },
                categoryNames,
                false,
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await userMutation.CommitAsync(cancellationToken);

            return true;
        }
    }
}
