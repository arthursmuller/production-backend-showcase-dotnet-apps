using Following.Domain.Repositories;
using Following.Application.Dtos;
using Following.Application.Providers.Categories;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record AddCategoryFollowCommand(
        IFollowingRepository Repository,
        IIdentityService Identity,
        CategoriesProvider Categories) : Command<AddCategoryFollowInput, CategoryFollow>
    {
        public override async Task<CategoryFollow> Handle(
            AddCategoryFollowInput input,
            CancellationToken cancellationToken)
        {
            FollowingInputLimits.ValidateCategoryName(input.Details.CategoryName);
            if (input.CategoryId.HasValue)
                FollowingInputLimits.RequirePositiveId(input.CategoryId.Value, nameof(input.CategoryId));

            var identity = Identity.User;
            if (identity?.Id is not > 0)
                throw new FollowingException("An authenticated user is required.", 403);
            var user = new StarterEntities.User(identity.Id, identity.Name);

            var categoryId = input.CategoryId ??
                await Categories.GetIdByName(input.Details.CategoryName, cancellationToken);

            await using var userMutation = await Repository.AcquireUserMutation(
                user.Id,
                cancellationToken);
            await Repository.EnsureUsersNotErased([user.Id], cancellationToken);
            var existing = await Repository.GetCategoryFollow(
                user.Id,
                categoryId,
                input.Details.CategoryName,
                cancellationToken);

            var follow = CategoryFollow.Create(
                user,
                categoryId,
                input.Details,
                existing);

            var analytics = await Repository.GetOrCreateAnalytics(
                user.Id, categoryId, cancellationToken);
            analytics.IncrementCategoryFollowings();

            await Repository.Add(follow, cancellationToken);
            await Categories.UpdateFollowAsync(
                new[] { categoryId },
                new[] { input.Details.CategoryName },
                true,
                cancellationToken);
            await Repository.SaveChanges(cancellationToken);
            await userMutation.CommitAsync(cancellationToken);

            return follow;
        }
    }
}
