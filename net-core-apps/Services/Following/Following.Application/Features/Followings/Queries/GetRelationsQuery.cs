using Following.Domain.Repositories;
using Following.Application.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Features.Followings
{
    public record GetRelationsQuery(
        IFollowingReadRepository Repository,
        IIdentityService Identity) : Query<int, GetRelationsDto>
    {
        public override async Task<GetRelationsDto> Handle(
            int id,
            CancellationToken cancellationToken)
        {
            FollowingInputLimits.RequirePositiveId(id, nameof(id));
            return GetRelationsDto.FromDomain(
                await Repository.GetRelations(Identity.User.Id, id, cancellationToken));
        }
    }
}
