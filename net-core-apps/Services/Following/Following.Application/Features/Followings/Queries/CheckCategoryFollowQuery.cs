using Following.Domain.Repositories;
using Following.Application.Dtos;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record CheckCategoryFollowQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity)
        : Query<CheckCategoryFollowDto, IEnumerable<CheckCategoryFollowResDto>>
    {
        public override async Task<IEnumerable<CheckCategoryFollowResDto>> Handle(
            CheckCategoryFollowDto dto,
            CancellationToken cancellationToken)
        {
            FollowingInputLimits.ValidateCategoryBatch(dto);
            var follows = await Repository.FindCategoryFollows(
                Identity.User.Id,
                dto.Ids,
                dto.Names,
                cancellationToken);

            return DtoConversor.Convert(dto, follows.ToList());
        }
    }
}
